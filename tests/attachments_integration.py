import io,json,struct,urllib.request,urllib.error,urllib.parse,uuid,zlib,zipfile
from pathlib import Path
from projects_integration import BASE,call,sql,user,token_for_admin,passed

def upload(path,token,files,expected=201,label=None):
    boundary='Boundary'+uuid.uuid4().hex;parts=[]
    for name,data in files:
        parts.append((f'--{boundary}\r\nContent-Disposition: form-data; name="files"; filename="{name}"\r\nContent-Type: application/octet-stream\r\n\r\n').encode()+data+b'\r\n')
    body=b''.join(parts)+f'--{boundary}--\r\n'.encode()
    headers={'Content-Type':'multipart/form-data; boundary='+boundary}
    if token:headers['Authorization']='Bearer '+token
    request=urllib.request.Request(BASE+path,data=body,headers=headers,method='POST')
    try:response=urllib.request.urlopen(request,timeout=60)
    except urllib.error.HTTPError as error:response=error
    raw=response.read();assert response.code==expected,f'{label}: {response.code}: {raw[:500]}'
    if label:passed.append(label);print('PASS',label,flush=True)
    return json.loads(raw) if raw else None

def read(path,token,expected=200,extra=None):
    # Cloud signed URLs are capability links; never forward application JWTs to another origin.
    local=not path.startswith('http') or urllib.parse.urlsplit(path).netloc==urllib.parse.urlsplit(BASE).netloc
    headers={'Authorization':'Bearer '+token} if token and local else {}
    headers.update(extra or {})
    try:r=urllib.request.urlopen(urllib.request.Request(path if path.startswith("http") else BASE+path,headers=headers),timeout=30)
    except urllib.error.HTTPError as error:r=error
    data=r.read();assert r.code==expected,f'{path}: {r.code}: {data[:400]}'
    return data,r.headers

def archive(main=None):
    out=io.BytesIO()
    with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as z:
        for name,data in [('[Content_Types].xml','<Types/>'),(main or 'hello.txt','<root/>')]:
            info=zipfile.ZipInfo(name,(2026,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;z.writestr(info,data)
    return out.getvalue()

def png():
    def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',1,1,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(b'\x00\xff\x00\x00\xff'))+chunk(b'IEND',b'')

def main():
    _,manager=user();author_id,author=user();other_id,other=user();_,outsider=user();admin=token_for_admin()
    project=call('POST','/api/v1/projects',manager,{'projectName':'Files '+uuid.uuid4().hex},201)['data']['projectId']
    for uid in [author_id,other_id]:call('POST',f'/api/v1/projects/{project}/members',manager,{'userId':uid},201)
    task=call('POST',f'/api/v1/projects/{project}/tasks',author,{'title':'Files'},201)['data']['taskId']
    path=f'/api/v1/tasks/{task}/attachments'
    upload(path,None,[('hello.txt',b'hello')],401,'anonymous upload denied')
    upload(path,outsider,[('hello.txt',b'hello')],403,'outsider upload denied')
    upload(path,admin,[('hello.txt',b'hello')],403,'Admin has no upload bypass')
    text='Nội dung kiểm thử'.encode()
    row=upload(path,author,[('hello.txt',text)],label='upload UTF-8 text')['data']['items'][0]
    aid=row['attachmentId'];detail=f'/api/v1/attachments/{aid}'
    assert 'filePath' not in row
    data,headers=read(detail+'/download',other)
    assert data==text and headers['X-Content-Type-Options']=='nosniff' and 'attachment' in headers['Content-Disposition']
    assert read(detail+'/preview',other)[0]==text
    assert read(detail+'/download',other,206,{'Range':'bytes=0-3'})[0]==text[:4]
    passed.append('authenticated download/preview/range preserve bytes, no path exposure')
    read(detail+'/download',None,401);read(detail+'/preview',outsider,403)
    passed.append('anonymous/outsider file access denied')
    call('DELETE',detail,other,expected=403,label='other Member cannot delete uploader file')
    upload(path,author,[('bad.exe',b'MZpayload')],400,'executable rejected')
    upload(path,author,[('bad.png',b'not an image')],400,'fake PNG rejected')
    upload(path,author,[('bad.jpg',png())],400,'image extension/content mismatch rejected')
    upload(path,author,[('bad.pdf',b'MZ not pdf')],400,'fake PDF rejected')
    upload(path,author,[('bad.docx',b'not zip')],400,'fake Office rejected')
    upload(path,author,[('bad.docx',archive('xl/workbook.xml'))],400,'wrong Office container rejected')
    macro=io.BytesIO(archive('word/document.xml'))
    with zipfile.ZipFile(macro,'a') as z:z.writestr('word/vbaProject.bin',b'macro')
    upload(path,author,[('macro.docx',macro.getvalue())],400,'macro-bearing Office rejected')
    oversized=bytearray(archive());offset=oversized.index(b'PK\x01\x02');struct.pack_into('<I',oversized,offset+24,268435457)
    upload(path,author,[('oversized.zip',bytes(oversized))],400,'ZIP expanded metadata limit enforced')
    upload(path,author,[('binary.txt',b'hello\x00binary')],400,'binary disguised as text rejected')
    upload(path,author,[('invalid.txt',b'\xff\xfe\xff')],400,'invalid text encoding rejected')
    upload(path,author,[('empty.txt',b'')],400,'empty file rejected')
    upload(path,author,[(f'{i}.txt',b'x') for i in range(11)],400,'more than ten files rejected')
    # Entire batch must roll back both metadata and bytes if a later file fails validation.
    root=Path(__file__).resolve().parents[1]/'backend/MyTaigaAPI/App_Data/test-task-files'
    before=set(root.glob('*'))
    count=sql(f'SELECT COUNT(*) FROM dbo.Attachment WHERE TaskId={task}')
    upload(path,author,[('valid.txt',b'ok'),('invalid.exe',b'MZ')],400,'invalid batch atomic rollback')
    assert sql(f'SELECT COUNT(*) FROM dbo.Attachment WHERE TaskId={task}')==count and set(root.glob('*'))==before
    passed.append('failed batch leaves no metadata or orphan files')
    sql("UPDATE dbo.SystemConfig SET ConfigValue='1' WHERE ConfigKey='MAX_FILE_SIZE_MB'")
    try:upload(path,author,[('large.txt',b'x'*(1048576+1))],400,'configured size limit enforced')
    finally:sql("UPDATE dbo.SystemConfig SET ConfigValue='25' WHERE ConfigKey='MAX_FILE_SIZE_MB'")
    sql("UPDATE dbo.SystemConfig SET ConfigValue='.exe,.txt' WHERE ConfigKey='ALLOWED_FILE_EXTENSIONS'")
    try:upload(path,author,[('bad.exe',b'MZ')],400,'config cannot enable executable uploads')
    finally:sql("UPDATE dbo.SystemConfig SET ConfigValue='.pdf,.docx,.xlsx,.pptx,.png,.jpg,.jpeg,.zip,.txt' WHERE ConfigKey='ALLOWED_FILE_EXTENSIONS'")
    rows=upload(path,author,[('pixel.png',png()),('document.docx',archive('word/document.xml')),('table.xlsx',archive('xl/workbook.xml')),
                             ('slides.pptx',archive('ppt/presentation.xml')),('archive.zip',archive()),('document.pdf',b'%PDF-1.4\n%%EOF\n')],label='valid image/Office/ZIP/PDF batch')['data']['items']
    image=next(r for r in rows if r['fileName']=='pixel.png')
    assert read(image['previewUrl'],other)[0]==png()
    office=next(r for r in rows if r['fileName']=='document.docx')
    read(office['previewUrl'],other,415);assert read(office['downloadUrl'],other)[0]==archive('word/document.xml')
    passed.append('image preview works; Office downloads but not unsafe inline preview')
    # Sanitize client paths; stored keys are generated and remain under the private root.
    sanitized=upload(path,author,[('../escape.txt',b'safe')],label='client path sanitized')['data']['items'][0]
    assert sanitized['fileName']=='escape.txt'
    assert call('GET',path+'?pageSize=1',other)['data']['total']==8 and len(call('GET',path+'?pageSize=1',other)['data']['items'])==1
    call('GET',path+'?pageSize=101',other,expected=400,label='attachment page size validation')
    key=sql(f'SELECT FilePath FROM dbo.Attachment WHERE AttachmentId={aid}')
    call('DELETE',detail,author,label='uploader removes file')
    assert not (root/key).exists();read(detail+'/download',author,404)
    call('DELETE',f"/api/v1/attachments/{image['attachmentId']}",manager,label='Manager can remove project file')
    # Missing disk file is a controlled 404, never a physical path response.
    missing=sql(f"SELECT FilePath FROM dbo.Attachment WHERE AttachmentId={office['attachmentId']}")
    (root/missing).unlink()
    read(office['downloadUrl'],other,404)
    passed.append('missing physical file is safe 404')
    call('PATCH',f'/api/v1/projects/{project}/close',manager)
    upload(path,author,[('closed.txt',b'closed')],409,'closed project upload denied')
    read(sanitized['downloadUrl'],other)
    call('DELETE',f"/api/v1/attachments/{sanitized['attachmentId']}",manager,expected=409,label='closed project deletion denied')
    call('DELETE',f'/api/v1/projects/{project}',manager)
    read(sanitized['downloadUrl'],other,404)
    passed.append('deleted project hides stored file')
    print(f'PASS ALL: {len(passed)} checks')

if __name__=='__main__':main()
