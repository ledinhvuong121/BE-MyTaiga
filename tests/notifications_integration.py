import time,uuid,concurrent.futures
from datetime import datetime,timezone
from projects_integration import call,sql,user,token_for_admin,passed
from signalr_client import SignalR

def main():
    owner_id,owner=user();member_id,member=user();_,outsider=user();admin=token_for_admin()
    project=call('POST','/api/v1/projects',owner,{'projectName':'Notifications '+uuid.uuid4().hex},201)['data']['projectId']
    call('POST',f'/api/v1/projects/{project}/members',owner,{'userId':member_id},201)
    task=call('POST',f'/api/v1/projects/{project}/tasks',member,{'title':'Notify'},201)['data']['taskId']
    call('GET','/api/v1/notifications',expected=401,label='anonymous inbox denied')
    call('POST','/hubs/notifications/negotiate?negotiateVersion=1',expected=401,label='anonymous SignalR denied')
    call('GET','/api/v1/users/me?access_token='+owner,expected=401,label='query token is not accepted on ordinary API')
    assert call('GET','/api/v1/notifications',outsider)['data']['total']==0
    assert call('GET','/api/v1/notifications/unread-count',owner)['data']['unreadCount']==0
    live=SignalR(owner);isolated=SignalR(outsider)
    bug=call('POST','/api/v1/bugs',member,{'taskId':task,'title':'Notify Manager','description':'Steps','severity':'MINOR'},201)['data']['bugId']
    event=live.event(timeout=15)
    assert event and event['refType']=='BUG' and event['refId']==bug
    assert isolated.event(timeout=1.5) is None
    passed.append('real WebSocket SignalR event reaches only correct recipient')
    rows=call('GET','/api/v1/notifications',owner)['data'];assert rows['total']==1
    nid=rows['items'][0]['notificationId'];assert event['notificationId']==nid
    call('PATCH',f'/api/v1/notifications/{nid}/read',outsider,expected=404,label='cannot read another user notification')
    assert call('GET','/api/v1/notifications/unread-count',owner)['data']['unreadCount']==1
    first=call('PATCH',f'/api/v1/notifications/{nid}/read',owner,label='mark own notification read')['data']
    repeat=call('PATCH',f'/api/v1/notifications/{nid}/read',owner,label='repeat read idempotent')['data']
    assert first['readAt']==repeat['readAt']
    assert call('GET','/api/v1/notifications?unreadOnly=true',owner)['data']['total']==0
    call('GET','/api/v1/notifications?pageSize=101',owner,expected=400,label='inbox page size validation')
    call('GET','/api/v1/notifications?page=2147483647',owner,expected=400,label='inbox pagination overflow')
    call('PATCH','/api/v1/notifications/0/read',owner,expected=400,label='invalid notification id')
    # New actor mentions Member; scope is checked again after membership is removed.
    call('POST',f'/api/v1/tasks/{task}/comments',owner,{'content':f'Hello @[Member](user:{member_id})'},201)
    member_rows=call('GET','/api/v1/notifications',member)['data'];assert member_rows['total']==1
    member_nid=member_rows['items'][0]['notificationId']
    call('DELETE',f'/api/v1/projects/{project}/members/{member_id}',owner)
    assert call('GET','/api/v1/notifications',member)['data']['total']==0
    call('PATCH',f'/api/v1/notifications/{member_nid}/read',member,expected=404,label='departed member notification content hidden')
    departed=SignalR(member)
    sql(f"INSERT dbo.Notification(UserId,Type,Title,RefType,RefId,CreatedAt) VALUES({member_id},'TASK_COMMENT','Hidden project content','TASK',{task},SYSUTCDATETIME())")
    assert departed.event(timeout=2.5) is None
    assert call('GET','/api/v1/notifications',member)['data']['total']==0
    departed.close();passed.append('outbox suppresses project content after membership loss')
    # Inbox global system notifications remain private and support read-all.
    sql(f"INSERT dbo.Notification(UserId,Type,Title,RefType,CreatedAt) VALUES({owner_id},'SYSTEM','Test system','SYSTEM',SYSUTCDATETIME())")
    assert call('PATCH','/api/v1/notifications/read-all',owner,label='mark all visible own notifications read')['data']['updated']==1
    assert call('GET','/api/v1/notifications/unread-count',owner)['data']['unreadCount']==0
    sql(f"INSERT dbo.Notification(UserId,Type,Title,RefType,CreatedAt) VALUES({owner_id},'SYSTEM','Concurrent read','SYSTEM',SYSUTCDATETIME())")
    concurrent_id=call('GET','/api/v1/notifications?unreadOnly=true',owner)['data']['items'][0]['notificationId']
    with concurrent.futures.ThreadPoolExecutor(2) as pool:
        results=list(pool.map(lambda _:call('PATCH',f'/api/v1/notifications/{concurrent_id}/read',owner)['data']['readAt'],range(2)))
    assert results[0]==results[1]
    passed.append('concurrent mark-read preserves one timestamp')
    call('GET',f'/api/v1/projects/{project}/activity-logs',outsider,expected=403,label='outsider audit history denied')
    call('GET',f'/api/v1/projects/{project}/activity-logs',admin,expected=403,label='Admin no project audit bypass')
    logs=call('GET',f'/api/v1/projects/{project}/activity-logs?entityType=BUG&action=BUG_CREATED',owner,label='filtered activity history')['data']
    assert logs['total']==1 and logs['items'][0]['entityId']==bug
    call('GET',f'/api/v1/projects/{project}/activity-logs?pageSize=101',owner,expected=400,label='history pagination validation')
    created=call('GET',f'/api/v1/projects/{project}/activity-logs?action=PROJECT_CREATED',owner)['data']['items'][0]['createdAt']
    assert abs((datetime.now(timezone.utc)-datetime.fromisoformat(created.replace('Z','+00:00'))).total_seconds())<60
    passed.append('project creation audit is recorded in UTC')
    # No public hub method permits joining arbitrary groups.
    isolated.send({'type':1,'invocationId':'1','target':'JoinUser','arguments':[owner_id]})
    denied=isolated.receive(5);assert denied and denied.get('type')==3 and 'error' in denied
    passed.append('cannot invoke arbitrary user-group subscription')
    # Remove queued system event if present, then test versioned group after logout.
    while live.event(timeout=.2) is not None:pass
    call('POST','/api/v1/auth/logout',owner)
    call('GET','/api/v1/notifications',owner,expected=401,label='revoked token inbox denied')
    call('POST','/hubs/notifications/negotiate?negotiateVersion=1',owner,expected=401,label='revoked token hub reconnect denied')
    # The removed member is re-added directly in the fixture, only for producing the new notification.
    sql(f"INSERT dbo.ProjectMember(ProjectId,UserId,RoleId) SELECT {project},{member_id},RoleId FROM dbo.Role WHERE RoleName='MEMBER' AND Scope='PROJECT'")
    call('POST','/api/v1/bugs',member,{'taskId':task,'title':'After logout','description':'Steps','severity':'MINOR'},201)
    assert live.event(timeout=2.5) is None
    passed.append('old SignalR group does not receive notifications after token-version change')
    live.close();isolated.close()
    print(f'PASS ALL: {len(passed)} checks')

if __name__=='__main__':main()
