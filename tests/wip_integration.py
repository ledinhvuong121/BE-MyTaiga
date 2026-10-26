"""WIP settings/alerts using real Board and Task APIs on disposable database."""
import uuid
from projects_integration import call,sql,user,token_for_admin,passed


def main():
    _,owner=user();member_id,member=user();_,outsider=user();admin=token_for_admin()
    pid=call('POST','/api/v1/projects',owner,{'projectName':'WIP '+uuid.uuid4().hex},201)['data']['projectId']
    other=call('POST','/api/v1/projects',outsider,{'projectName':'Other WIP '+uuid.uuid4().hex},201)['data']['projectId']
    call('POST',f'/api/v1/projects/{pid}/members',owner,{'userId':member_id},201)
    root=f'/api/v1/projects/{pid}'
    columns=call('GET',root+'/board',member)['data']['columns']
    todo=next(c for c in columns if c['columnName']=='TODO')['boardColumnId']
    active=next(c for c in columns if c['columnName']=='IN_PROGRESS')['boardColumnId']
    foreign_col=call('GET',f'/api/v1/projects/{other}/board',outsider)['data']['columns'][0]['boardColumnId']
    settings=root+f'/board/columns/{todo}/wip-limit'
    call('GET',root+'/wip',expected=401,label='anonymous WIP read denied')
    call('GET',root+'/wip',outsider,expected=403,label='outsider WIP read denied')
    call('GET',root+'/wip',admin,expected=403,label='Admin WIP read denied')
    assert all(c['wipLimit'] is None and c['taskCount']==0 for c in call('GET',root+'/wip',member)['data']['columns'])
    call('PUT',settings,member,{'wipLimit':2},403,'Member WIP set denied')
    call('DELETE',settings,member,expected=403,label='Member WIP removal denied')
    call('PUT',settings,outsider,{'wipLimit':2},403,'outsider WIP set denied')
    call('PUT',settings,admin,{'wipLimit':2},403,'Admin WIP set denied')
    call('PUT',settings,owner,{'wipLimit':0},400,'zero limit rejected')
    call('PUT',settings,owner,{'wipLimit':-1},400,'negative limit rejected')
    call('PUT',settings,owner,{},400,'missing limit rejected')
    call('PUT',root+f'/board/columns/{foreign_col}/wip-limit',owner,{'wipLimit':2},404,'foreign column update denied')
    call('DELETE',root+f'/board/columns/{foreign_col}/wip-limit',owner,expected=404,label='foreign column delete denied')
    cards=[call('POST',root+'/tasks',member,{'title':'WIP card'},201)['data']['taskId'] for _ in range(3)]
    set_result=call('PUT',settings,owner,{'wipLimit':2},label='lower limit shows existing overload')['data']
    assert set_result['taskCount']==3 and set_result['isOverloaded'] and set_result['changed']
    assert call('GET',root+'/wip/alerts',member)['data']['columns'][0]['excess']==1
    board=call('GET',root+'/board?keyword=none',member)['data']['columns'][0]
    assert board['isOverloaded'] and board['matchingCount']==0 and board['totalCount']==3
    passed.append('overload not hidden by Board filters')
    assert call('PUT',settings,owner,{'wipLimit':2},label='idempotent setting')['data']['changed'] is False
    assert sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE ProjectId={pid} AND EntityId={todo} AND Action='WIP_LIMIT_SET'")=='1'
    call('POST',root+'/tasks',member,{'title':'Blocked'},409,'TODO WIP blocks creation')
    call('PATCH',f'/api/v1/tasks/{cards[0]}/move',member,{'toStatus':'TODO','newPosition':2},label='overloaded column reorder allowed')
    call('PATCH',f'/api/v1/tasks/{cards[0]}/move',member,{'toStatus':'IN_PROGRESS','newPosition':0},label='move out of overloaded column')
    assert call('GET',root+'/wip/alerts',member)['data']['columns']==[]
    todo_row=next(c for c in call('GET',root+'/wip',member)['data']['columns'] if c['columnName']=='TODO')
    assert todo_row['atCapacity'] and not todo_row['isOverloaded']
    passed.append('at capacity distinct from overload')
    call('PATCH',f'/api/v1/tasks/{cards[0]}/move',member,{'toStatus':'TODO','newPosition':0},409,'full destination blocks move')
    call('PUT',root+f'/board/columns/{active}/wip-limit',owner,{'wipLimit':1},label='set second column limit')
    call('PATCH',f'/api/v1/tasks/{cards[1]}/move',member,{'toStatus':'IN_PROGRESS','newPosition':1},409,'active WIP blocks move')
    call('DELETE',settings,owner,label='remove WIP limit')
    assert next(c for c in call('GET',root+'/wip',member)['data']['columns'] if c['columnName']=='TODO')['wipLimit'] is None
    call('POST',root+'/tasks',member,{'title':'Allowed again'},201,'creation allowed after removing limit')
    call('DELETE',settings,owner,label='idempotent limit removal')
    assert sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE ProjectId={pid} AND EntityId={todo} AND Action='WIP_LIMIT_REMOVED'")=='1'
    call('GET','/api/v1/projects/0/wip',owner,expected=400,label='invalid project WIP')
    call('PUT',root+'/board/columns/0/wip-limit',owner,{'wipLimit':2},400,'invalid column id')
    call('PATCH',root+'/close',owner)
    call('PUT',settings,owner,{'wipLimit':2},409,'closed project WIP change denied')
    call('DELETE',settings,owner,expected=409,label='closed project WIP removal denied')
    call('GET',root+'/wip',member,label='closed project WIP readable')
    call('DELETE',root,owner)
    call('GET',root+'/wip',member,expected=404,label='deleted project WIP hidden')
    call('GET',root+'/wip/alerts',member,expected=404,label='deleted project WIP alerts hidden')
    print(f'PASS ALL: {len(passed)} checks')


if __name__=='__main__':main()
