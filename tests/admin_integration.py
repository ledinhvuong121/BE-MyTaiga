import uuid
from projects_integration import call,sql,user,token_for_admin,passed

def main():
    uid,u=user();mid,m=user();admin=token_for_admin()
    call('GET','/api/v1/admin/users',expected=401,label='anonymous admin denied')
    for path in ['users','roles','system-config','activity-logs']:
        call('GET','/api/v1/admin/'+path,u,expected=403,label='USER cannot read admin '+path)
    roles=call('GET','/api/v1/admin/roles',admin,label='system roles list')['data'];assert {r['roleName'] for r in roles}=={'ADMIN','USER'}
    email=uuid.uuid4().hex+'@example.com';body={'email':email,'fullName':'Admin created','password':'TestOnly!2026','confirmPassword':'TestOnly!2026'}
    created=call('POST','/api/v1/admin/users',admin,body,201,label='Admin creates USER')['data'];newid=created['userId']
    assert created['systemRole']=='USER'
    call('POST','/api/v1/admin/users',admin,body,409,label='duplicate account rejected')
    call('POST','/api/v1/admin/users',u,{**body,'email':'other'+email},403,label='USER cannot create admin account')
    listing=call('GET','/api/v1/admin/users?keyword='+email,admin,label='admin search users')['data'];assert listing['total']==1 and 'passwordHash' not in str(listing)
    call('GET','/api/v1/admin/users?status=2',admin,expected=400,label='invalid status filter')
    call('GET','/api/v1/admin/users?page=2147483647',admin,expected=400,label='admin pagination overflow')
    call('PUT',f'/api/v1/admin/users/{uid}/role',admin,{'role':'ADMIN'},409,label='cannot create second ADMIN')
    aid=int(sql("SET NOCOUNT ON;SELECT ur.UserId FROM dbo.UserRole ur JOIN dbo.Role r ON r.RoleId=ur.RoleId WHERE r.RoleName='ADMIN'"))
    call('PUT',f'/api/v1/admin/users/{aid}/role',admin,{'role':'USER'},409,label='cannot demote only ADMIN')
    call('PUT',f'/api/v1/admin/users/{uid}/role',admin,{'role':'MANAGER'},400,label='project role is not system role')
    call('PUT',f'/api/v1/admin/users/{uid}/role',admin,{'role':'USER'},label='same system role idempotent')
    call('PUT','/api/v1/admin/users/999999999/role',admin,{'role':'USER'},404,label='missing user role target')
    original={r['configKey']:r['configValue'] for r in call('GET','/api/v1/admin/system-config',admin)['data']}
    try:
        call('PUT','/api/v1/admin/system-config',u,{'values':{'MAX_COMMENT_LENGTH':'50'}},403,label='USER cannot change config')
        call('PUT','/api/v1/admin/system-config',admin,{'values':{'MAX_FILE_SIZE_MB':'26'}},400,label='file size cap enforced')
        call('PUT','/api/v1/admin/system-config',admin,{'values':{'Jwt:Key':'no'}},400,label='secrets cannot be configured via API')
        call('PUT','/api/v1/admin/system-config',admin,{'values':{'ALLOWED_FILE_EXTENSIONS':'.exe'}},400,label='unsafe extension config rejected')
        call('PUT','/api/v1/admin/system-config',admin,{'values':{'MAX_COMMENT_LENGTH':'50','MIN_PASSWORD_LENGTH':'1'}},400,label='invalid batch is all-or-nothing')
        assert {r['configKey']:r['configValue'] for r in call('GET','/api/v1/admin/system-config',admin)['data']}==original
        call('PUT','/api/v1/admin/system-config',admin,{'values':{'MAX_COMMENT_LENGTH':'50'}},label='valid configuration update')
        assert int(sql("SET NOCOUNT ON;SELECT ConfigValue FROM dbo.SystemConfig WHERE ConfigKey='MAX_COMMENT_LENGTH'"))==50
    finally:call('PUT','/api/v1/admin/system-config',admin,{'values':original})
    p=call('POST','/api/v1/projects',u,{'projectName':'Permissions '+uuid.uuid4().hex},201)['data']['projectId']
    call('POST',f'/api/v1/projects/{p}/members',u,{'userId':mid},201)
    manager=call('GET',f'/api/v1/me/permissions?projectId={p}',u,label='manager permissions scope')['data']
    member=call('GET',f'/api/v1/me/permissions?projectId={p}',m,label='member permissions scope')['data']
    assert manager['projectRole']=='MANAGER' and set(member['projectPermissions'])<set(manager['projectPermissions'])
    assert 'WIP_MANAGE' in manager['projectPermissions'] and 'WIP_MANAGE' not in member['projectPermissions']
    call('GET',f'/api/v1/me/permissions?projectId={p}',admin,expected=403,label='ADMIN no project permissions bypass')
    perms=call('GET','/api/v1/me/permissions',admin,label='ADMIN system permissions')['data'];assert perms['systemRole']=='ADMIN' and perms['projectPermissions']==[]
    call('GET','/api/v1/admin/activity-logs',admin,label='system audit history')
    assert sql(f"SET NOCOUNT ON;SELECT COUNT(*) FROM dbo.ActivityLog WHERE Action='ADMIN_USER_CREATED' AND UserId={aid} AND EntityId={newid}")=='1'
    passed.append('admin creation audit identifies actor and target')
    print(f'PASS ALL: {len(passed)} checks')
if __name__=='__main__':main()
