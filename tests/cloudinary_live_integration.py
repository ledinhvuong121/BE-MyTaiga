"""Opt-in live Cloudinary checks; SQL must use a disposable Checks database."""
import sys,json,uuid,urllib.request
from pathlib import Path
from projects_integration import call,sql,user,token_for_admin,BASE,DATABASE
from attachments_integration import upload,read,png

def main():
    if '--allow-real-cloud' not in sys.argv: raise SystemExit('Explicit --allow-real-cloud required; consumes Cloudinary quota.')
    call('GET','/api/v1/users/me',token_for_admin())
    uid,owner=user();mid,member=user();_,outsider=user()
    assets=[];checks=[]
    (Path(__file__).resolve().parents[1]/'backend/MyTaigaAPI/App_Data/test-cloudinary').mkdir(parents=True,exist_ok=True)
    def passed(label):checks.append(label);print('PASS',label,flush=True)
    def save():
        (Path(__file__).resolve().parents[1]/'backend/MyTaigaAPI/App_Data/test-cloudinary/cloudinary-live-result.json').write_text(json.dumps({'database':DATABASE,'checks':checks,'cleanupAssets':assets},indent=2),encoding='utf-8')
    boundary='Avatar'+uuid.uuid4().hex
    body=(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="avatar.png"\r\nContent-Type: image/png\r\n\r\n').encode()+png()+f'\r\n--{boundary}--\r\n'.encode()
    request=urllib.request.Request(BASE+'/api/v1/users/me/avatar',data=body,method='PUT',headers={'Authorization':'Bearer '+owner,'Content-Type':'multipart/form-data; boundary='+boundary})
    result=json.loads(urllib.request.urlopen(request,timeout=60).read());avatar=result['data']['avatar'];assets.append(avatar);save()
    assert avatar.startswith('https://res.cloudinary.com/') and read(avatar,None)[0].startswith(b'\x89PNG')
    assert sql(f'SELECT Avatar FROM dbo.[User] WHERE UserId={uid}')==avatar
    passed('avatar API uploads real cloud image; DB stores URL; public delivery 200')
    pid=call('POST','/api/v1/projects',owner,{'projectName':'Cloud live '+uuid.uuid4().hex},201)['data']['projectId']
    call('POST',f'/api/v1/projects/{pid}/members',owner,{'userId':mid},201)
    tid=call('POST',f'/api/v1/projects/{pid}/tasks',owner,{'title':'Real cloud image'},201)['data']['taskId']
    image=upload(f'/api/v1/tasks/{tid}/attachments',owner,[('live.png',png())])['data']['items'][0]
    stored=sql(f"SELECT FilePath FROM dbo.Attachment WHERE AttachmentId={image['attachmentId']}");assets.append(stored);save()
    assert stored.startswith('https://res.cloudinary.com/') and '/authenticated/' in stored and '/s--' not in stored
    assert read(image['previewUrl'],None)[0]==png() and read(image['downloadUrl'],None)[0]==png()
    passed('Task image API stores unsigned cloud URL; signed preview/download return correct bytes')
    try:urllib.request.urlopen(stored,timeout=30);raise AssertionError('Private image exposed')
    except urllib.error.HTTPError as error:assert error.code in (401,403,404)
    passed('unsigned private Task cloud URL denies anonymous access')
    call('GET',f"/api/v1/attachments/{image['attachmentId']}",outsider,expected=403)
    call('GET',f"/api/v1/attachments/{image['attachmentId']}",expected=401)
    passed('Task metadata rejects outsider and anonymous requests')
    rid=call('POST','/api/v1/chat/rooms',owner,{'roomType':'PRIVATE','recipientId':mid},201)['data']['chatRoomId']
    chat=upload(f'/api/v1/chat/rooms/{rid}/messages',member,[('live-chat.png',png())])['data']['attachments'][0]
    stored=sql(f"SELECT TOP(1) FilePath FROM dbo.MessageAttachment ORDER BY MessageAttachmentId DESC");assets.append(stored);save()
    assert '/authenticated/' in stored and '/s--' not in stored and read(chat['previewUrl'],None)[0]==png() and read(chat['downloadUrl'],None)[0]==png()
    passed('Chat image API stores cloud URL; signed preview/download return correct bytes')
    save();print('PASS ALL:',len(checks),'live API checks; cleanup manifest saved without credentials')
if __name__=='__main__':main()

