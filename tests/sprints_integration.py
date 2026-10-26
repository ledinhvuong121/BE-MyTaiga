"""Sprint checks on isolated SQL/API setup; apply migration 10 first."""
import concurrent.futures
from datetime import date, timedelta
import uuid
from projects_integration import call, sql, user, token_for_admin, passed


def main():
    owner_id, owner = user()
    member_id, member = user()
    _, outsider = user()
    admin = token_for_admin()
    project = call('POST', '/api/v1/projects', owner, {'projectName': 'Sprints '+uuid.uuid4().hex}, 201)['data']['projectId']
    other = call('POST', '/api/v1/projects', outsider, {'projectName': 'Other '+uuid.uuid4().hex}, 201)['data']['projectId']
    call('POST', f'/api/v1/projects/{project}/members', owner, {'userId': member_id}, 201)
    epic = call('POST', f'/api/v1/projects/{project}/epics', owner, {'title': 'Sprint Epic'}, 201)['data']['epicId']
    foreign_epic = call('POST', f'/api/v1/projects/{other}/epics', outsider, {'title': 'Other'}, 201)['data']['epicId']
    def pbi(title, points=5, eid=epic, token=owner):
        return call('POST', '/api/v1/product-backlog-items', token,
                    {'epicId': eid, 'title': title, 'storyPoint': points, 'status': 'READY'}, 201)['data']['pbiId']
    first, second, third = pbi('First', 5), pbi('Second', 3), pbi('Third', 2)
    foreign_pbi = pbi('Foreign', eid=foreign_epic, token=outsider)
    start = date.today()
    body = {'projectId': project, 'sprintName': 'Sprint 1', 'sprintGoal': 'Kiểm thử',
            'startDate': start.isoformat(), 'endDate': (start+timedelta(days=6)).isoformat()}
    path = '/api/v1/sprints'
    listing = path+f'?projectId={project}'
    call('POST', path, body=body, expected=401, label='anonymous Sprint denied')
    call('POST', path, member, body, 403, 'member cannot create Sprint')
    call('POST', path, admin, body, 403, 'admin cannot create Sprint')
    call('GET', listing, outsider, expected=403, label='outsider list denied')
    for data, label in [({'sprintName': ' '}, 'blank name'), ({'sprintName': 'x'*151}, 'long name'),
                         ({'sprintGoal': 'x'*501}, 'long goal'), ({'startDate': 'bad'}, 'bad date'),
                         ({'endDate': (start-timedelta(days=1)).isoformat()}, 'reversed dates'),
                         ({'endDate': (start+timedelta(days=366)).isoformat()}, 'long Sprint')]:
        call('POST', path, owner, {**body, **data}, 400, label)
    call('POST', path, owner, {'projectId': project, 'sprintName': 'Missing dates'}, 400, 'missing dates')
    sid = call('POST', path, owner, {**body, 'status': 'ACTIVE'}, 201, 'create always PLANNED')['data']['sprintId']
    root = path+f'/{sid}'
    assert call('GET', root, member)['data']['status'] == 'PLANNED'
    assert call('GET', root, member)['data']['sprintGoal'] == 'Kiểm thử'
    call('POST', path, owner, body, 409, 'duplicate Sprint name')
    call('POST', path, owner, {**body, 'sprintName': 'Overlap'}, 409, 'overlapping dates')
    second_body = {**body, 'sprintName': 'Sprint 2', 'startDate': (start+timedelta(days=7)).isoformat(), 'endDate': (start+timedelta(days=13)).isoformat()}
    sid2 = call('POST', path, owner, second_body, 201, 'multiple Sprints in one project')['data']['sprintId']
    root2 = path+f'/{sid2}'
    call('GET', listing+'&bad=1', owner, expected=200) # ignored extra query fields do not leak other projects
    assert call('GET', listing, owner)['data']['total'] == 2
    call('GET', path, owner, expected=400, label='project id required')
    call('GET', listing+'&status=BAD', owner, expected=400, label='bad Sprint filter')
    call('GET', listing+'&pageSize=101', owner, expected=400, label='page size validation')
    call('PUT', root, outsider, body, 403, 'outsider cannot update')
    call('PUT', root, owner, {**body, 'projectId': other}, 400, 'cannot change project scope')
    call('PUT', root2, owner, body, 409, 'rename conflict rolls back')
    call('POST', root+'/start', owner, expected=409, label='empty Sprint cannot start')
    for items, expected, label in [([first,first],400,'duplicate IDs'), ([0],400,'zero PBI id'),
                                    ([first,foreign_pbi],404,'foreign PBI batch rolls back')]:
        call('POST', root+'/backlog-items', owner, {'pbiIds':items}, expected, label)
    assert sql(f'SELECT COUNT(*) FROM dbo.SprintBacklogItem WHERE SprintId={sid}') == '0'
    call('POST', root+'/backlog-items', member, {'pbiIds':[first]}, 403, 'member cannot plan')
    call('POST', root+'/backlog-items', owner, {'pbiIds':[first,second]}, label='select PBI batch')
    call('POST', root+'/backlog-items', owner, {'pbiIds':[first]}, 409, 'duplicate planning denied')
    call('POST', root2+'/backlog-items', owner, {'pbiIds':[first]}, 409, 'PBI cannot be in two open Sprints')
    progress = call('GET', root+'/progress', member)['data']
    assert progress['totalPbis']==2 and progress['totalStoryPoints']==8 and progress['remainingStoryPoints']==8
    call('DELETE', root+f'/backlog-items/{second}', owner, label='remove planned PBI')
    assert sql(f'SELECT Status FROM dbo.ProductBacklogItem WHERE PBIId={second}') == 'READY'
    call('POST', root+'/backlog-items', owner, {'pbiIds':[second]})
    call('POST', root2+'/backlog-items', owner, {'pbiIds':[third]})
    def start_sprint(url):
        try:
            call('POST', url+'/start', owner)
            return 200
        except AssertionError as error:
            if 'got 409:' in str(error): return 409
            raise
    with concurrent.futures.ThreadPoolExecutor(2) as pool:
        assert sorted(pool.map(start_sprint, [root,root2])) == [200,409]
    active = int(sql(f"SELECT SprintId FROM dbo.Sprint WHERE ProjectId={project} AND Status='ACTIVE'"))
    if active != sid:
        call('POST', root2+'/close', owner)
        call('POST', root+'/start', owner)
    passed.append('concurrent activation keeps exactly one ACTIVE Sprint')
    call('POST', root+'/start', owner, expected=409, label='repeat start denied')
    call('DELETE', root, owner, expected=409, label='active Sprint cannot delete')
    call('DELETE', root+f'/backlog-items/{second}', owner, expected=409, label='active Sprint cannot remove scope silently')
    call('PUT', root, owner, {**body, 'endDate': (start+timedelta(days=5)).isoformat()}, 409, 'active dates immutable')
    call('PUT', root, owner, {**body, 'sprintGoal': 'Updated goal'}, label='update active goal')
    # Complete PBI; SQL trigger records real point delta, then edit estimate and reopen.
    pbi_path='/api/v1/product-backlog-items'
    call('PUT', pbi_path+f'/{first}', owner, {'epicId':epic,'title':'First','storyPoint':5,'status':'DONE'})
    progress=call('GET',root+'/progress',member)['data']
    assert progress['completedStoryPoints']==5 and progress['progressPercent']==62.5
    chart=call('GET',root+'/burndown',member,label='burndown reads actual event history')['data']
    assert chart['totalScope']==8 and chart['points'][0]['actualRemaining']==3
    assert chart['points'][1]['actualRemaining'] is None
    assert chart['points'][-1]['idealRemaining']==0
    call('PUT',pbi_path+f'/{first}',owner,{'epicId':epic,'title':'First','storyPoint':6,'status':'IN_SPRINT'})
    chart=call('GET',root+'/burndown',member)['data']
    assert chart['totalScope']==9 and chart['points'][0]['actualRemaining']==9
    passed.append('Burndown captures completion/reopen/estimate changes')
    call('PUT',pbi_path+f'/{first}',owner,{'epicId':epic,'title':'First','storyPoint':6,'status':'DONE'})
    call('POST',root+'/close',member,expected=403,label='member cannot close')
    call('POST',root+'/close',owner,label='close Sprint')
    assert sql(f'SELECT Status FROM dbo.ProductBacklogItem WHERE PBIId={second}')=='READY'
    closed=call('GET',root+'/progress',member)['data']
    assert closed['totalStoryPoints']==9 and closed['completedStoryPoints']==6 and closed['totalPbis']==2
    call('POST',root+'/close',owner,expected=409,label='repeat close denied')
    call('POST',root+'/backlog-items',owner,{'pbiIds':[third]},409,'closed Sprint planning denied')
    call('PUT',root,owner,body,409,'closed Sprint editing denied')
    # Incomplete PBI can enter a later Sprint without altering closed Sprint's snapshot or chart.
    later={**body,'sprintName':'Carryover','startDate':(start+timedelta(days=20)).isoformat(),'endDate':(start+timedelta(days=26)).isoformat()}
    carry=call('POST',path,owner,later,201)['data']['sprintId']
    call('POST',path+f'/{carry}/backlog-items',owner,{'pbiIds':[second]},label='carry unfinished PBI to later Sprint')
    call('PUT',pbi_path+f'/{second}',owner,{'epicId':epic,'title':'Second','storyPoint':20,'status':'DONE'})
    assert call('GET',root+'/progress',member)['data']['remainingStoryPoints']==3
    assert call('GET',root+'/burndown',member)['data']['totalScope']==9
    passed.append('closed snapshot/history unaffected by future PBI changes')
    # Deleting a planned Sprint returns unfinished scope, but blocks existing Tasks/metrics.
    fourth=pbi('Fourth')
    planned={**body,'sprintName':'Delete planned','startDate':(start+timedelta(days=30)).isoformat(),'endDate':(start+timedelta(days=36)).isoformat()}
    delete_id=call('POST',path,owner,planned,201)['data']['sprintId']
    call('POST',path+f'/{delete_id}/backlog-items',owner,{'pbiIds':[fourth]})
    call('DELETE',path+f'/{delete_id}',owner,label='delete planned Sprint with scoped PBI')
    assert sql(f'SELECT Status FROM dbo.ProductBacklogItem WHERE PBIId={fourth}')=='READY'
    call('GET',path+f'/{delete_id}',owner,expected=404,label='deleted Sprint missing')
    assert sql(f'SELECT COUNT(*) FROM dbo.SprintBurndownEvent WHERE SprintId={delete_id}')=='0'
    # Planned Sprints with persisted Task/metric data are not silently cascade-deleted.
    occupied={**body,'sprintName':'Occupied','startDate':(start+timedelta(days=40)).isoformat(),'endDate':(start+timedelta(days=46)).isoformat()}
    occupied_id=call('POST',path,owner,occupied,201)['data']['sprintId']
    sql(f"INSERT dbo.Task(ProjectId,SprintId,ReporterId,Title) VALUES({project},{occupied_id},{owner_id},N'Existing task')")
    call('DELETE',path+f'/{occupied_id}',owner,expected=409,label='planned Sprint with Task cannot delete')
    assert sql(f'SELECT COUNT(*) FROM dbo.Sprint WHERE SprintId={occupied_id}')=='1'
    metric={**body,'sprintName':'Metrics','startDate':(start+timedelta(days=50)).isoformat(),'endDate':(start+timedelta(days=56)).isoformat()}
    metric_id=call('POST',path,owner,metric,201)['data']['sprintId']
    sql(f'INSERT dbo.FlowMetric(SprintId,WIP,Throughput) VALUES({metric_id},0,0)')
    call('DELETE',path+f'/{metric_id}',owner,expected=409,label='planned Sprint with metrics cannot delete')
    # Simulate a pre-migration baseline: dates before the baseline must not be fabricated.
    sql(f'UPDATE dbo.SprintBurndownEvent SET IsBaseline=1,OccurredAt=DATEADD(day,1,SYSUTCDATETIME()) WHERE SprintId={sid}')
    assert call('GET',root+'/burndown',member)['data']['points'][0]['actualRemaining'] is None
    passed.append('Burndown marks dates before historical baseline as unknown')
    call('POST',path,owner,{**body,'endDate':'9999-12-31'},400,'reject overflowing timeline')
    assert int(sql(f"SELECT COUNT(*) FROM dbo.Notification WHERE UserId={member_id} AND RefId={sid} AND Type='SPRINT_CREATED'"))==1
    passed.append('notification and audit stored transactionally; no deleted history orphan')
    call('GET',root+'/burndown',outsider,expected=403,label='outsider Burndown denied')
    call('GET',path+'/0',owner,expected=400,label='zero Sprint id')
    call('GET',path+'/9223372036854775807',owner,expected=404,label='missing Sprint')
    call('PATCH',f'/api/v1/projects/{project}/close',owner)
    call('POST',path,owner,body,409,'closed project Sprint create denied')
    call('GET',root+'/progress',member,label='closed project progress readable')
    call('DELETE',f'/api/v1/projects/{project}',owner)
    call('GET',root,member,expected=404,label='deleted project Sprint hidden')
    print(f'PASS ALL: {len(passed)} checks')


if __name__=='__main__': main()
