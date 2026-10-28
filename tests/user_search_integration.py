import uuid,urllib.parse
from projects_integration import call,sql,user,token_for_admin,passed

def main():
    admin=token_for_admin();call('GET','/api/v1/users/me',admin)
    uid,token=user();other,_=user();marker='search-'+uuid.uuid4().hex
    sql(f"UPDATE dbo.[User] SET FullName=N'Lê {marker}',Email='{marker}@example.com' WHERE UserId={uid}")
    query='/api/v1/users?keyword='+urllib.parse.quote(marker)
    call('GET',query,expected=401,label='anonymous search denied')
    result=call('GET',query,token,label='USER searches email and name')['data']
    assert result['total']==1 and result['items'][0]['userId']==uid
    assert set(result['items'][0])=={'userId','fullName','email','avatarUrl'}
    passed.append('response only exposes directory fields')
    assert call('GET',query,admin,label='ADMIN can use directory')['data']['total']==1
    assert call('GET','/api/v1/users?keyword='+urllib.parse.quote('Lê '+marker),token)['data']['total']==1
    passed.append('Vietnamese full-name search')
    assert call('GET','/api/v1/users?keyword='+urllib.parse.quote(marker+'@example.com'),token)['data']['total']==1
    passed.append('email-only search')
    assert call('GET','/api/v1/users?keyword='+urllib.parse.quote('  '+marker+'  '),token)['data']['total']==1
    passed.append('keyword whitespace trimmed')
    assert call('GET','/api/v1/users?keyword='+uuid.uuid4().hex,token)['data']['items']==[]
    passed.append('no matches returns empty page')
    sql(f'UPDATE dbo.[User] SET Status=0 WHERE UserId={other}')
    assert all(row['userId']!=other for row in call('GET','/api/v1/users?pageSize=100',token)['data']['items'])
    passed.append('locked accounts excluded')
    a=call('GET','/api/v1/users?pageSize=1',token)['data'];b=call('GET','/api/v1/users?pageSize=1&page=2',token)['data']
    assert len(a['items'])==1 and len(b['items'])==1 and a['items'][0]['userId']!=b['items'][0]['userId']
    passed.append('pagination is deterministic and bounded')
    for suffix in ['page=0','pageSize=0','pageSize=101','page=2147483647','keyword='+'x'*201]:
        call('GET','/api/v1/users?'+suffix,token,expected=400,label='invalid query rejected: '+suffix[:25])
    call('GET','/api/v1/admin/users',token,expected=403,label='USER still cannot access admin directory')
    call('GET','/api/v1/users/me',token,label='profile route still works')
    print('PASS ALL:',len(passed),'checks')
if __name__=='__main__':main()
