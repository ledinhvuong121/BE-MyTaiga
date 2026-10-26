"""Task APIs and SQL rules. Disposable database; migrations 10 and 11 required."""
import concurrent.futures
from datetime import datetime, timedelta, timezone
import json
import urllib.request
import urllib.error
import uuid
from projects_integration import BASE, call, sql, user, token_for_admin, passed


def main():
    owner_id,owner=user(); member_id,member=user(); outsider_id,outsider=user(); locked_id,_=user()
    sql(f'UPDATE dbo.[User] SET Status=0 WHERE UserId={locked_id}')
    admin=token_for_admin()
    pid=call('POST','/api/v1/projects',owner,{'projectName':'Task '+uuid.uuid4().hex},201)['data']['projectId']
    foreign=call('POST','/api/v1/projects',outsider,{'projectName':'Foreign '+uuid.uuid4().hex},201)['data']['projectId']
    call('POST',f'/api/v1/projects/{pid}/members',owner,{'userId':member_id},201)
    epic=call('POST',f'/api/v1/projects/{pid}/epics',owner,{'title':'Task Epic'},201)['data']['epicId']
    foreign_epic=call('POST',f'/api/v1/projects/{foreign}/epics',outsider,{'title':'Foreign'},201)['data']['epicId']
    today=datetime.now(timezone.utc).date()
    sb={'projectId':pid,'sprintName':'Tasks','startDate':today.isoformat(),'endDate':(today+timedelta(days=6)).isoformat()}
    sprint=call('POST','/api/v1/sprints',owner,sb,201)['data']['sprintId']
    foreign_sprint=call('POST','/api/v1/sprints',outsider,{**sb,'projectId':foreign},201)['data']['sprintId']
    root=f'/api/v1/projects/{pid}/tasks'
    body={'title':'Task first','sprintId':sprint,'epicId':epic}
    call('POST',root,body=body,expected=401,label='anonymous Task create denied')
    call('POST',root,outsider,body,403,'outsider Task create denied')
    call('POST',root,admin,body,403,'admin has no Task bypass')
    for change,label in [({'title':' '},'blank title'),({'title':'x'*256},'long title'),
                          ({'description':'x'*10001},'long description'),({'priority':'BAD'},'invalid priority'),
                          ({'sprintId':0},'invalid Sprint id')]:
        call('POST',root,owner,{**body,**change},400,label)
    call('POST',root,member,{**body,'priority':'HIGH'},403,'Member cannot set priority')
    call('POST',root,member,{**body,'assigneeId':member_id},403,'Member cannot self-assign via create')
    call('POST',root,member,{**body,'deadline':'2026-12-01T00:00:00Z'},403,'Member cannot set deadline')
    call('POST',root,owner,{**body,'assigneeId':outsider_id},409,'foreign assignee denied')
    call('POST',root,owner,{**body,'assigneeId':locked_id},409,'locked assignee denied')
    call('POST',root,owner,{**body,'epicId':foreign_epic},404,'cross-project Epic denied')
    call('POST',root,owner,{**body,'sprintId':foreign_sprint},409,'cross-project Sprint denied')
    created=call('POST',root,member,{**body,'reporterId':outsider_id,'status':'DONE'},201,'Member creates ordinary Task')['data']
    tid=created['taskId']; detail=f'/api/v1/tasks/{tid}'
    assert created['reporterId']==member_id and created['status']=='TODO' and created['progressPercent']==0
    assert created['createdAt'].endswith('Z') and created['updatedAt'].endswith('Z')
    call('GET',detail,outsider,expected=403,label='outsider detail denied')
    call('GET',detail,expected=401,label='anonymous detail denied')
    call('PUT',detail,member,{'title':'Updated','description':'Tiếng Việt'},label='Member edits Task')
    call('PATCH',detail+'/progress',member,{'progressPercent':55},label='Member reports progress')
    assert call('GET',detail,owner)['data']['task']['progressPercent']==55
    call('PATCH',detail+'/progress',member,{'progressPercent':100},400,'completion must use Board DONE')
    call('PATCH',detail+'/progress',member,{'progressPercent':-1},400,'negative progress denied')
    call('PATCH',detail+'/assignee',member,{'assigneeId':member_id},403,'Member assign denied')
    call('PATCH',detail+'/priority',member,{'priority':'HIGH'},403,'Member priority denied')
    call('PATCH',detail+'/deadline',member,{'deadline':None},403,'Member deadline denied')
    call('POST',detail+'/subtasks',member,{'title':'Denied child'},403,'Member Subtask denied')
    call('PATCH',detail+'/assignee',owner,{'assigneeId':member_id},label='Manager assigns member')
    call('PATCH',detail+'/assignee',owner,{'assigneeId':member_id},409,'repeat assignment conflict')
    call('PATCH',detail+'/assignee',owner,{'assigneeId':outsider_id},409,'foreign reassignment denied')
    call('PATCH',detail+'/priority',owner,{'priority':'CRITICAL'},label='Manager sets priority')
    call('PATCH',detail+'/deadline',owner,{'deadline':'2026-12-01T10:00:00+07:00'},label='Manager sets offset deadline')
    assert call('GET',detail,member)['data']['task']['deadline']=='2026-12-01T03:00:00Z'
    call('PATCH',detail+'/deadline',owner,{'deadline':None},label='Manager clears deadline')
    assert call('GET',detail,member)['data']['task']['deadline'] is None
    child=call('POST',detail+'/subtasks',owner,{'title':'Child'},201,'Manager creates Subtask')['data']
    assert child['parentTaskId']==tid and child['sprintId']==sprint and child['epicId']==epic
    call('POST',detail+'/subtasks',owner,{'title':'Bad child','epicId':foreign_epic},400,'Subtask scope mismatch')
    foreign_task=call('POST',f'/api/v1/projects/{foreign}/tasks',outsider,{'title':'Foreign'},201)['data']['taskId']
    call('POST',root,owner,{**body,'parentTaskId':foreign_task},404,'cross-project parent denied')
    call('DELETE',detail,member,expected=409,label='cannot delete parent with Subtask')
    assert len(call('GET',detail,member)['data']['subtasks'])==1
    call('DELETE',f"/api/v1/tasks/{child['taskId']}",member,label='Member deletes leaf Task')
    call('GET',root+'?page=0',member,expected=400,label='invalid Task page')
    call('GET',root+'?status=BAD',member,expected=400,label='invalid Board status filter')
    call('GET',root+f'?sprintId={foreign_sprint}',member,expected=404,label='cross-project Sprint filter denied')
    assert call('GET',root+f'?assigneeId={member_id}&sprintId={sprint}&keyword=Updated',member)['data']['total']==1
    # Use actual SQL column transitions until Board API is implemented in the next branch.
    sql(f"UPDATE dbo.Task SET Status='IN_PROGRESS' WHERE TaskId={tid}")
    started=call('GET',detail,member)['data']['task']['startedAt']
    assert abs((datetime.now(timezone.utc)-datetime.fromisoformat(started.replace('Z','+00:00'))).total_seconds())<30
    sql(f"UPDATE dbo.Task SET Status='DONE' WHERE TaskId={tid}")
    done=call('GET',detail,member)['data']['task']
    assert done['progressPercent']==100 and done['completedAt'] is not None
    call('PATCH',detail+'/progress',member,{'progressPercent':90},409,'DONE progress protected')
    call('POST',detail+'/subtasks',owner,{'title':'Done child'},409,'DONE parent cannot receive Subtask')
    sql(f"UPDATE dbo.Task SET Status='TODO' WHERE TaskId={tid}")
    reopened=call('GET',detail,member)['data']['task']
    assert reopened['startedAt']==started and reopened['completedAt'] is None and reopened['progressPercent']==0
    passed.append('UTC first start survives reopen; completion and progress reset')
    # Overdue and due-soon Manager alerts, completed items excluded.
    overdue=call('POST',root,owner,{'title':'Late','deadline':(datetime.now(timezone.utc)-timedelta(hours=1)).isoformat()},201)['data']['taskId']
    soon=call('POST',root,owner,{'title':'Soon','deadline':(datetime.now(timezone.utc)+timedelta(hours=1)).isoformat()},201)['data']['taskId']
    call('GET',root+'/overdue',member,expected=403,label='Member cannot read Manager overdue report')
    assert call('GET',root+'/overdue',owner)['data']['total']==1
    alerts=call('GET',root+'/deadline-alerts',owner,label='Manager deadline alerts')['data']
    assert {x['alert'] for x in alerts['items']}=={'OVERDUE','DUE_SOON'}
    sql(f"UPDATE dbo.Task SET Status='DONE' WHERE TaskId={overdue}")
    assert call('GET',root+'/overdue',owner)['data']['total']==0
    # Referential safety: never silently destroy comments/files/bugs.
    for table,statement in [('Comment',f"INSERT dbo.Comment(TaskId,UserId,Content) VALUES({soon},{owner_id},N'Comment')"),
                             ('Attachment',f"INSERT dbo.Attachment(TaskId,FileName,FilePath,FileSize,UploadedBy) VALUES({soon},N'test.txt',N'test-only-path',1,{owner_id})"),
                             ('Bug',f"INSERT dbo.Bug(TaskId,ReporterId,Title,Description,Severity) VALUES({soon},{owner_id},N'Bug',N'Description','MINOR')")]:
        sql(statement)
        call('DELETE',f'/api/v1/tasks/{soon}',owner,expected=409,label='delete blocked by '+table)
        sql(f'DELETE FROM dbo.[{table}] WHERE TaskId={soon}')
    # Historical reporter can leave without breaking a future title edit.
    historical=call('POST',root,member,{'title':'History'},201)['data']['taskId']
    sql(f"UPDATE dbo.Task SET Status='DONE' WHERE TaskId={tid}")
    call('DELETE',f'/api/v1/projects/{pid}/members/{member_id}',owner)
    call('PUT',f'/api/v1/tasks/{historical}',owner,{'title':'Reporter history retained'},label='former reporter preserved after leaving')
    call('GET',detail,member,expected=403,label='removed Member loses Task access')
    call('POST',f'/api/v1/projects/{pid}/members',owner,{'userId':member_id},201)
    # Close Sprint with planned PBI; Task becomes read-only.
    pbi=call('POST','/api/v1/product-backlog-items',owner,{'epicId':epic,'title':'Sprint PBI','storyPoint':1},201)['data']['pbiId']
    call('POST',f'/api/v1/sprints/{sprint}/backlog-items',owner,{'pbiIds':[pbi]})
    call('POST',f'/api/v1/sprints/{sprint}/start',owner)
    call('POST',f'/api/v1/sprints/{sprint}/close',owner)
    call('PUT',detail,owner,{'title':'Closed'},409,'closed Sprint Task read-only')
    call('POST',root,owner,body,409,'closed Sprint create denied')
    # Project lock serializes creation: WIP capacity cannot be exceeded concurrently.
    capacity=call('POST','/api/v1/projects',owner,{'projectName':'Capacity '+uuid.uuid4().hex},201)['data']['projectId']
    sql(f"UPDATE dbo.BoardColumn SET WipLimit=1 WHERE ProjectId={capacity} AND ColumnName='TODO'")
    def create_capacity(_):
        req=urllib.request.Request(BASE+f'/api/v1/projects/{capacity}/tasks',data=json.dumps({'title':'Concurrent'}).encode(),
            headers={'Content-Type':'application/json','Authorization':'Bearer '+owner},method='POST')
        try:
            with urllib.request.urlopen(req,timeout=30) as response:response.read();return response.code
        except urllib.error.HTTPError as error:error.read();return error.code
    with concurrent.futures.ThreadPoolExecutor(2) as pool:assert sorted(pool.map(create_capacity,range(2)))==[201,409]
    passed.append('concurrent creation cannot exceed TODO WIP')
    assert sql(f"SELECT COUNT(*) FROM dbo.Notification WHERE RefId={tid} AND Type='TASK_ASSIGNED' AND UserId={member_id}")=='1'
    passed.append('assignment notification stored once, failed assignment no side effects')
    call('DELETE',f'/api/v1/tasks/{historical}',member,label='Member deletes simple Task')
    call('GET',f'/api/v1/tasks/{historical}',owner,expected=404,label='deleted Task missing')
    call('GET','/api/v1/tasks/0',owner,expected=400,label='invalid Task id')
    call('GET','/api/v1/tasks/9223372036854775807',owner,expected=404,label='missing Task')
    call('PATCH',f'/api/v1/projects/{pid}/close',owner)
    call('POST',root,member,{'title':'Closed'},409,'closed project Task create denied')
    call('GET',detail,member,label='closed project Task remains readable')
    call('DELETE',f'/api/v1/projects/{pid}',owner)
    call('GET',detail,member,expected=404,label='deleted project Task hidden')
    print(f'PASS ALL: {len(passed)} checks')


if __name__=='__main__':main()
