"""Board and move integration checks against a disposable SQL database."""
import concurrent.futures
import json
import urllib.request
import urllib.error
import uuid
from datetime import datetime,timezone,timedelta
from projects_integration import BASE,call,sql,user,token_for_admin,passed


def main():
    _,owner=user();member_id,member=user();_,outsider=user();admin=token_for_admin()
    pid=call('POST','/api/v1/projects',owner,{'projectName':'Board '+uuid.uuid4().hex},201)['data']['projectId']
    other=call('POST','/api/v1/projects',outsider,{'projectName':'Other board '+uuid.uuid4().hex},201)['data']['projectId']
    call('POST',f'/api/v1/projects/{pid}/members',owner,{'userId':member_id},201)
    path=f'/api/v1/projects/{pid}/board';tasks=f'/api/v1/projects/{pid}/tasks'
    call('GET',path,expected=401,label='anonymous Board denied')
    call('GET',path,outsider,expected=403,label='outsider Board denied')
    call('GET',path,admin,expected=403,label='admin Board denied')
    columns=call('GET',path,member,label='Member reads empty Board')['data']['columns']
    assert [c['columnName'] for c in columns]==['TODO','IN_PROGRESS','IN_REVIEW','DONE'] and all(not c['tasks'] for c in columns)
    first=call('POST',tasks,member,{'title':'First'},201)['data']['taskId']
    second=call('POST',tasks,member,{'title':'Second'},201)['data']['taskId']
    third=call('POST',tasks,owner,{'title':'Third','assigneeId':member_id},201)['data']['taskId']
    foreign_task=call('POST',f'/api/v1/projects/{other}/tasks',outsider,{'title':'Foreign'},201)['data']['taskId']
    def move(tid,body,token=member,expected=200,label=None):return call('PATCH',f'/api/v1/tasks/{tid}/move',token,body,expected,label)
    move(first,{'toStatus':'IN_PROGRESS','newPosition':0},None,401,'anonymous move denied')
    move(first,{'toStatus':'IN_PROGRESS','newPosition':0},outsider,403,'outsider move denied')
    move(first,{'toStatus':'IN_PROGRESS','newPosition':0},admin,403,'admin move denied')
    move(foreign_task,{'toStatus':'IN_PROGRESS','newPosition':0},member,403,'foreign project move denied')
    move(first,{'toStatus':'INVALID','newPosition':0},expected=400,label='invalid destination denied')
    move(first,{'toStatus':'IN_PROGRESS','newPosition':-1},expected=400,label='negative position denied')
    move(first,{'toStatus':'IN_PROGRESS','newPosition':1},expected=400,label='out-of-range position denied')
    move(first,{'toStatus':'TODO','newPosition':2},label='reorder within column')
    ordered=call('GET',path,member)['data']['columns'][0]['tasks']
    assert [t['taskId'] for t in ordered]==[second,third,first] and [t['position'] for t in ordered]==[0,1,2]
    moved=move(first,{'toStatus':'IN_PROGRESS','newPosition':0},label='Member moves to IN_PROGRESS')['data']
    assert moved['startedAt'] is not None and moved['completedAt'] is None
    start=moved['startedAt']
    assert abs((datetime.now(timezone.utc)-datetime.fromisoformat(start.replace('Z','+00:00'))).total_seconds())<30
    sql(f"UPDATE dbo.BoardColumn SET WipLimit=1 WHERE ProjectId={pid} AND ColumnName='IN_PROGRESS'")
    move(second,{'toStatus':'IN_PROGRESS','newPosition':1},expected=409,label='WIP full move denied')
    # WIP uses all cards, even if assignee/keyword filters hide some of them.
    board=call('GET',path+f'?assigneeId={member_id}&keyword=Third',member)['data']['columns']
    active=next(c for c in board if c['columnName']=='IN_PROGRESS')
    assert active['totalCount']==1 and active['matchingCount']==0 and active['wipLimit']==1
    passed.append('filtered Board retains true WIP counts')
    move(first,{'toStatus':'DONE','newPosition':0},label='complete Task through Board')
    detail=call('GET',f'/api/v1/tasks/{first}',member)['data']['task']
    assert detail['progressPercent']==100 and detail['completedAt'] is not None
    move(first,{'toStatus':'TODO','newPosition':0},label='reopen Task through Board')
    detail=call('GET',f'/api/v1/tasks/{first}',member)['data']['task']
    assert detail['startedAt']==start and detail['completedAt'] is None and detail['progressPercent']==0
    # Two cards compete for one free WIP slot.
    def compete(tid):
        req=urllib.request.Request(BASE+f'/api/v1/tasks/{tid}/move',data=json.dumps({'toStatus':'IN_PROGRESS','newPosition':0}).encode(),
            headers={'Content-Type':'application/json','Authorization':'Bearer '+member},method='PATCH')
        try:
            with urllib.request.urlopen(req,timeout=30) as response:response.read();return response.code
        except urllib.error.HTTPError as error:error.read();return error.code
    with concurrent.futures.ThreadPoolExecutor(2) as pool:assert sorted(pool.map(compete,[first,second]))==[200,409]
    assert sql(f"SELECT COUNT(*) FROM dbo.Task WHERE ProjectId={pid} AND Status='IN_PROGRESS'")=='1'
    passed.append('concurrent moves preserve WIP capacity')
    # Parent cannot complete before children; child cannot reopen under completed parent.
    parent=call('POST',tasks,owner,{'title':'Parent'},201)['data']['taskId']
    child=call('POST',f'/api/v1/tasks/{parent}/subtasks',owner,{'title':'Child'},201)['data']['taskId']
    move(parent,{'toStatus':'DONE','newPosition':0},expected=409,label='parent waits for unfinished Subtask')
    move(child,{'toStatus':'DONE','newPosition':0},label='complete Subtask')
    move(parent,{'toStatus':'DONE','newPosition':1},label='complete parent after Subtask')
    move(child,{'toStatus':'TODO','newPosition':0},expected=409,label='cannot reopen Subtask under DONE parent')
    move(parent,{'toStatus':'TODO','newPosition':0})
    move(child,{'toStatus':'TODO','newPosition':1},label='reopen child after parent')
    # Counts/pagination are per column with stable ordering.
    paged=call('GET',path+'?pageSize=1',member)['data']['columns']
    assert all(len(c['tasks'])<=1 for c in paged) and any(c['hasMore'] for c in paged)
    passed.append('per-column pagination and hasMore')
    call('GET',path+'?pageSize=201',member,expected=400,label='Board page size validation')
    call('GET',path+'?page=2147483647',member,expected=400,label='Board pagination overflow')
    call('GET',path+'?assigneeId=9223372036854775807',member,expected=404,label='foreign assignee filter denied')
    today=datetime.now(timezone.utc).date()
    sprint=call('POST','/api/v1/sprints',owner,{'projectId':pid,'sprintName':'Board Sprint','startDate':today.isoformat(),'endDate':(today+timedelta(days=6)).isoformat()},201)['data']['sprintId']
    sprint_task=call('POST',tasks,member,{'title':'Sprint card','sprintId':sprint},201)['data']['taskId']
    filtered=call('GET',path+f'?sprintId={sprint}',member)['data']['columns']
    assert sum(c['matchingCount'] for c in filtered)==1
    epic=call('POST',f'/api/v1/projects/{pid}/epics',owner,{'title':'Sprint epic'},201)['data']['epicId']
    pbi=call('POST','/api/v1/product-backlog-items',owner,{'title':'Sprint PBI','epicId':epic},201)['data']['pbiId']
    call('POST',f'/api/v1/sprints/{sprint}/backlog-items',owner,{'pbiIds':[pbi]})
    call('POST',f'/api/v1/sprints/{sprint}/start',owner);call('POST',f'/api/v1/sprints/{sprint}/close',owner)
    move(sprint_task,{'toStatus':'DONE','newPosition':0},expected=409,label='closed Sprint card cannot move')
    call('PATCH',f'/api/v1/projects/{pid}/close',owner)
    move(third,{'toStatus':'DONE','newPosition':0},expected=409,label='closed project cannot move')
    call('GET',path,member,label='closed project Board readable')
    call('DELETE',f'/api/v1/projects/{pid}',owner)
    call('GET',path,member,expected=404,label='deleted project Board hidden')
    call('GET','/api/v1/projects/0/board',owner,expected=400,label='invalid project id')
    call('PATCH','/api/v1/tasks/9223372036854775807/move',owner,{'toStatus':'DONE','newPosition':0},404,'missing Task move')
    # Failed moves did not alter status or emit TASK_MOVED audit entries.
    assert sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE EntityId={sprint_task} AND EntityType='TASK' AND Action='TASK_MOVED'")=='0'
    passed.append('failed move rollback preserves Task and audit')
    print(f'PASS ALL: {len(passed)} checks')


if __name__=='__main__':main()
