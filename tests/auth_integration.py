import uuid,re,time,urllib.request,urllib.error,json,concurrent.futures
from pathlib import Path
from projects_integration import call,sql,token_for_admin,passed,BASE,user
from attachments_integration import png,read

def main():
    email='auth-'+uuid.uuid4().hex+'@example.com';password='Original!2026';new='Changed!2026';reset='ResetFinal!2026';admin=token_for_admin()
    body={'fullName':'Auth test','email':email,'password':password,'confirmPassword':password}
    uid=call('POST','/api/v1/auth/register',body=body,expected=201,label='register actual database user')['data']['userId']
    assert sql(f"SELECT PasswordHash FROM dbo.[User] WHERE UserId={uid}").startswith('$2');passed.append('password stored as BCrypt not plaintext')
    call('POST','/api/v1/auth/register',body=body,expected=409,label='duplicate register rejected')
    call('POST','/api/v1/auth/register',body={**body,'email':'invalid'},expected=400,label='email syntax validation')
    call('POST','/api/v1/auth/register',body={**body,'confirmPassword':'Mismatch!'},expected=400,label='registration password confirmation')
    call('POST','/api/v1/auth/login',body={'email':email,'password':'wrong'},expected=401,label='wrong password rejected')
    assert sql(f'SELECT FailedLoginCount FROM dbo.[User] WHERE UserId={uid}')=='1'
    token=call('POST','/api/v1/auth/login',body={'email':email,'password':password},label='correct password login')['data']['accessToken']
    assert sql(f'SELECT FailedLoginCount FROM dbo.[User] WHERE UserId={uid}')=='0'
    call('GET','/api/v1/users/me',expected=401,label='profile requires JWT')
    profile=call('GET','/api/v1/users/me',token,label='profile own identity')['data'];assert profile['userId']==uid
    call('PUT','/api/v1/users/me',token,{'fullName':'Updated test','email':email,'phoneNumber':'0900000000'},label='profile update persists')
    assert call('GET','/api/v1/users/me',token)['data']['fullName']=='Updated test'
    call('PUT','/api/v1/users/me',token,{'fullName':'Updated','email':email,'phoneNumber':'abc'},400,label='phone validation')
    def avatar(data,expected):
        boundary=uuid.uuid4().hex
        raw=(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="avatar.png"\r\nContent-Type: image/png\r\n\r\n').encode()+data+f'\r\n--{boundary}--\r\n'.encode()
        req=urllib.request.Request(BASE+'/api/v1/users/me/avatar',data=raw,method='PUT',headers={'Authorization':'Bearer '+token,'Content-Type':'multipart/form-data; boundary='+boundary})
        try:r=urllib.request.urlopen(req)
        except urllib.error.HTTPError as e:r=e
        result=r.read();assert r.code==expected,result[:400]
        import json
        return json.loads(result)
    result=avatar(png(),200);avatar_url=result['data']['avatar']
    assert avatar_url.startswith('https://res.cloudinary.com/')
    assert sql(f'SELECT Avatar FROM dbo.[User] WHERE UserId={uid}')==avatar_url
    passed.append('avatar upload returns Cloudinary URL and database stores URL only')
    assert '/image/upload/' in avatar_url
    passed.append('avatar URL is public identity image for member rendering')
    avatar(b'not a png',400);passed.append('avatar rejects invalid content')
    avatar(b'x'*(5*1024*1024+1),413);passed.append('avatar size limit')
    call('POST','/api/v1/auth/change-password',token,{'currentPassword':'wrong','newPassword':new,'confirmPassword':new},400,label='change requires correct old password')
    call('POST','/api/v1/auth/change-password',token,{'currentPassword':password,'newPassword':new,'confirmPassword':new},label='password change succeeds')
    call('GET','/api/v1/users/me',token,expected=401,label='change revokes old JWT')
    call('POST','/api/v1/auth/login',body={'email':email,'password':password},expected=401,label='old password no longer works')
    token=call('POST','/api/v1/auth/login',body={'email':email,'password':new})['data']['accessToken']
    def request_reset():
        call('POST','/api/v1/auth/forgot-password',body={'email':email})
        folder=Path(__file__).resolve().parents[1]/'backend/MyTaigaAPI/App_Data/test-emails'
        content=next(p.read_text(encoding='utf-8-sig') for p in sorted(folder.glob('*.txt'),key=lambda p:p.stat().st_mtime,reverse=True) if ('To: '+email) in p.read_text(encoding='utf-8-sig'))
        return re.search(r'token=([A-Fa-f0-9]{64})',content).group(1)
    first=request_reset();second=request_reset()
    call('POST','/api/v1/auth/reset-password',body={'token':first,'newPassword':reset,'confirmPassword':reset},expected=400,label='new request invalidates old reset token')
    call('POST','/api/v1/auth/reset-password',body={'token':second,'newPassword':reset,'confirmPassword':'different'},expected=400,label='reset confirmation required')
    call('POST','/api/v1/auth/reset-password',body={'token':second,'newPassword':reset,'confirmPassword':reset},label='reset via emailed link token succeeds')
    call('POST','/api/v1/auth/reset-password',body={'token':second,'newPassword':reset,'confirmPassword':reset},expected=400,label='reset token single use')
    call('GET','/api/v1/users/me',token,expected=401,label='reset revokes prior JWT')
    token=call('POST','/api/v1/auth/login',body={'email':email,'password':reset})['data']['accessToken']
    third=request_reset();sql(f'UPDATE dbo.PasswordResetToken SET CreatedAt=DATEADD(minute,-2,SYSUTCDATETIME()),ExpiresAt=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE UserId={uid} AND IsUsed=0')
    call('POST','/api/v1/auth/reset-password',body={'token':third,'newPassword':new,'confirmPassword':new},expected=400,label='expired reset token rejected')
    unknown=call('POST','/api/v1/auth/forgot-password',body={'email':uuid.uuid4().hex+'@example.com'})
    known=call('POST','/api/v1/auth/forgot-password',body={'email':email});assert known==unknown;passed.append('forgot response does not reveal account existence')
    call('PATCH',f'/api/v1/admin/users/{uid}/lock',admin,label='Admin locks user')
    call('GET','/api/v1/users/me',token,expected=401,label='locked user JWT denied')
    call('PATCH',f'/api/v1/admin/users/{uid}/unlock',admin,label='Admin unlocks user')
    call('GET','/api/v1/users/me',token,expected=401,label='unlock does not restore old token')
    token=call('POST','/api/v1/auth/login',body={'email':email,'password':reset})['data']['accessToken']
    call('POST','/api/v1/auth/logout',token,label='logout succeeds')
    call('GET','/api/v1/users/me',token,expected=401,label='logout revokes JWT')
    for attempt in range(5):
        call('POST','/api/v1/auth/login',body={'email':email,'password':'wrong'},expected=403 if attempt==4 else 401)
    assert sql(f'SELECT Status FROM dbo.[User] WHERE UserId={uid}')=='0'
    passed.append('five failed logins automatically lock account')
    call('POST','/api/v1/auth/login',body={'email':email,'password':reset},expected=403,label='locked user cannot login with correct password')
    call('PATCH',f'/api/v1/admin/users/{uid}/unlock',admin)
    shared=request_reset()
    def concurrent_reset(_):
        req=urllib.request.Request(BASE+'/api/v1/auth/reset-password',data=json.dumps({'token':shared,'newPassword':new,'confirmPassword':new}).encode(),headers={'Content-Type':'application/json'},method='POST')
        try:
            with urllib.request.urlopen(req) as response:return response.code
        except urllib.error.HTTPError as e:return e.code
    with concurrent.futures.ThreadPoolExecutor(2) as pool:assert sorted(pool.map(concurrent_reset,range(2)))==[200,400]
    passed.append('simultaneous reset consumes token exactly once')
    for origin,allowed in [('http://localhost:5173',True),('https://untrusted.example',False)]:
        req=urllib.request.Request(BASE+'/api/v1/users/me',method='OPTIONS',headers={'Origin':origin,'Access-Control-Request-Method':'PUT','Access-Control-Request-Headers':'authorization,content-type'})
        with urllib.request.urlopen(req) as r:assert (r.headers.get('Access-Control-Allow-Origin')==origin)==allowed
    passed.append('CORS allows configured frontend only')
    print(f'PASS ALL: {len(passed)} checks')
if __name__=='__main__':main()
