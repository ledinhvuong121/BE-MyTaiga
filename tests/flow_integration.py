"""Known-answer Flow Metrics tests; disposable database with migration 12."""
from datetime import datetime,timedelta,timezone
import uuid
from projects_integration import call,sql,user,token_for_admin,passed


def main():
    _,owner=user();member_id,member=user();_,outsider=user();admin=token_for_admin()
    pid=call('POST','/api/v1/projects',owner,{'projectName':'Flow '+uuid.uuid4().hex},201)['data']['projectId']
    other=call('POST','/api/v1/projects',outsider,{'projectName':'Other flow '+uuid.uuid4().hex},201)['data']['projectId']
    call('POST',f'/api/v1/projects/{pid}/members',owner,{'userId':member_id},201)
    root=f'/api/v1/projects/{pid}/flow-metrics'
    now=datetime.now(timezone.utc).replace(microsecond=0);today=now.date()
    def sprint(project,name,offset,token=owner):
        return call('POST','/api/v1/sprints',token,{'projectId':project,'sprintName':name,
            'startDate':(today+timedelta(days=offset)).isoformat(),'endDate':(today+timedelta(days=offset+5)).isoformat()},201)['data']['sprintId']
    sid=sprint(pid,'First',-40);sid2=sprint(pid,'Second',-33);foreign=sprint(other,'Foreign',-40,outsider)
    def task(title,status='TODO',started=None,completed=None,sprint_id=None):
        tid=call('POST',f'/api/v1/projects/{pid}/tasks',owner,{'title':title,'sprintId':sprint_id},201)['data']['taskId']
        def stamp(value):return 'NULL' if value is None else "'"+value.strftime('%Y-%m-%dT%H:%M:%S')+"'"
        sql(f"UPDATE dbo.Task SET Status='{status}',StartedAt={stamp(started)},CompletedAt={stamp(completed)} WHERE TaskId={tid}")
        return tid
    endpoints=['wip','throughput','throughput/sprints','cycle-time','cycle-time/average','work-item-age']
    for endpoint in endpoints:
        call('GET',root+'/'+endpoint,expected=401,label='anonymous denied '+endpoint)
        call('GET',root+'/'+endpoint,member,expected=403,label='Member denied '+endpoint)
        call('GET',root+'/'+endpoint,outsider,expected=403,label='outsider denied '+endpoint)
        call('GET',root+'/'+endpoint,admin,expected=403,label='Admin denied '+endpoint)
    assert call('GET',root+'/wip',owner)['data']['wip']==0
    empty=call('GET',root+'/cycle-time/average',owner)['data']
    assert empty['validTasks']==0 and empty['averageCycleTimeDays'] is None
    assert call('GET',root+'/work-item-age',owner)['data']['total']==0
    assert call('GET',root+'/throughput',owner)['data']['totalDoneTasks']==0
    passed.append('empty metrics: zero counts, undefined cycle average null')
    active=task('Active','IN_PROGRESS',now-timedelta(days=2),sprint_id=sid)
    review=task('Review','IN_REVIEW',now-timedelta(days=4),sprint_id=sid2)
    stale=task('Reopened TODO','TODO',now-timedelta(days=10))
    task('Not started')
    done=task('Done A','DONE',now-timedelta(days=3),now-timedelta(days=1),sid)
    task('Done B','DONE',now-timedelta(days=6),now-timedelta(days=2),sid2)
    missing=task('Missing start','DONE',now-timedelta(days=4),now-timedelta(days=3))
    sql(f'UPDATE dbo.Task SET StartedAt=NULL WHERE TaskId={missing}')
    task('Future completed','DONE',now-timedelta(days=1),now+timedelta(days=2))
    task('Future started','IN_PROGRESS',now+timedelta(days=1))
    activity_before=sql(f'SELECT COUNT(*) FROM dbo.ActivityLog WHERE ProjectId={pid}')
    wip=call('GET',root+'/wip',owner,label='WIP counts only working/review columns')['data']
    assert wip['wip']==3 and wip['inProgress']==2 and wip['inReview']==1
    assert call('GET',root+f'/wip?sprintId={sid}',owner)['data']['wip']==1
    assert call('GET',root+f'/wip?sprintId={sid2}',owner)['data']['inReview']==1
    data=call('GET',root+'/cycle-time',owner,label='per-Task Cycle Time known answers')['data']
    times={x['title']:x['cycleTimeDays'] for x in data['items']}
    assert times['Done A']==2 and times['Done B']==4 and times['Missing start'] is None and times['Future completed'] is None
    avg=call('GET',root+'/cycle-time/average',owner,label='Cycle Time average excludes invalid timestamps')['data']
    assert avg['validTasks']==2 and avg['skippedInvalidTasks']==2 and avg['averageCycleTimeDays']==3
    assert call('GET',root+f'/cycle-time/average?sprintId={sid}',owner)['data']['averageCycleTimeDays']==2
    ages=call('GET',root+'/work-item-age',owner,label='age excludes DONE, not-started and future-started')['data']
    assert ages['total']==3
    known={x['taskId']:x['workItemAgeDays'] for x in ages['items']}
    assert abs(known[active]-2)<0.002 and abs(known[review]-4)<0.002 and abs(known[stale]-10)<0.002
    old=call('GET',root+'/work-item-age?minAgeDays=7',owner,label='find stale work by age threshold')['data']
    assert old['total']==1 and old['items'][0]['taskId']==stale
    assert call('GET',root+'/work-item-age?minAgeDays=2.5',owner)['data']['total']==2
    throughput=call('GET',root+'/throughput',owner,label='first-completion Throughput known count')['data']
    assert throughput['totalDoneTasks']==3 and len(throughput['points'])==7
    assert sum(p['doneTasks'] for p in throughput['points'])==3
    assert throughput['historyTrackingStartedAtUtc'].endswith('Z') and throughput['hasPartialHistoricalCoverage']
    assert throughput['points'][0]['historyComplete'] is False
    passed.append('partial pre-migration history is explicitly flagged')
    assert call('GET',root+f'/throughput?sprintId={sid}',owner)['data']['totalDoneTasks']==1
    compared=call('GET',root+'/throughput/sprints',owner,label='compare Sprint Throughput')['data']
    assert {x['sprintId']:x['doneTasks'] for x in compared['items']}=={sid:1,sid2:1}
    assert len(call('GET',root+'/throughput/sprints?pageSize=1',owner)['data']['items'])==1
    sql(f'EXEC dbo.sp_CaptureFlowMetric @SprintId={sid}')
    assert sql(f'SELECT TOP(1) WIP FROM dbo.FlowMetric WHERE SprintId={sid} ORDER BY FlowMetricId DESC')=='1'
    assert sql(f'SELECT TOP(1) Throughput FROM dbo.FlowMetric WHERE SprintId={sid} ORDER BY FlowMetricId DESC')=='1'
    assert float(sql(f'SELECT TOP(1) CycleTime FROM dbo.FlowMetric WHERE SprintId={sid} ORDER BY FlowMetricId DESC'))==2
    assert abs(float(sql(f'SELECT TOP(1) AvgWorkItemAge FROM dbo.FlowMetric WHERE SprintId={sid} ORDER BY FlowMetricId DESC'))-2)<0.02
    assert int(sql(f'SELECT TOP(1) ABS(DATEDIFF(SECOND,CapturedAt,SYSUTCDATETIME())) FROM dbo.FlowMetric WHERE SprintId={sid} ORDER BY FlowMetricId DESC'))<30
    passed.append('legacy snapshot procedure stores correct metrics and UTC capture time')
    assert sql(f'SELECT COUNT(*) FROM dbo.ActivityLog WHERE ProjectId={pid}')==activity_before
    passed.append('GET metrics have no audit/data mutations')
    for endpoint in ['wip','throughput','cycle-time','cycle-time/average','work-item-age']:
        call('GET',root+f'/{endpoint}?sprintId={foreign}',owner,expected=404,label='foreign Sprint denied '+endpoint)
        call('GET',root+f'/{endpoint}?sprintId=0',owner,expected=400,label='invalid Sprint denied '+endpoint)
    for query,label in [('from=bad','invalid date'),('from='+today.isoformat()+'&to='+(today-timedelta(days=1)).isoformat(),'reversed interval'),
                         ('from='+(today-timedelta(days=366)).isoformat(),'too long interval'),
                         ('to='+(today+timedelta(days=1)).isoformat(),'future interval'),('to=9999-12-31','overflow interval')]:
        call('GET',root+'/throughput?'+query,owner,expected=400,label=label)
    call('GET',root+'/work-item-age?minAgeDays=-0.1',owner,expected=400,label='negative fractional threshold rejected')
    call('GET',root+'/work-item-age?minAgeDays=36500.1',owner,expected=400,label='large fractional threshold rejected')
    call('GET',root+'/cycle-time?pageSize=101',owner,expected=400,label='cycle page size validation')
    call('GET',root+'/work-item-age?page=2147483647',owner,expected=400,label='age pagination overflow')
    # Reopen/recomplete/deletion must not rewrite historic throughput or duplicate first completion.
    call('PATCH',f'/api/v1/tasks/{done}/move',owner,{'toStatus':'TODO','newPosition':0})
    assert call('GET',root+'/throughput',owner)['data']['totalDoneTasks']==3
    assert call('GET',root+'/cycle-time/average',owner)['data']['validTasks']==1
    call('PATCH',f'/api/v1/tasks/{done}/move',owner,{'toStatus':'DONE','newPosition':0})
    assert call('GET',root+'/throughput',owner)['data']['totalDoneTasks']==3
    assert sql(f'SELECT COUNT(*) FROM dbo.TaskCompletionHistory WHERE TaskId={done}')=='1'
    call('DELETE',f'/api/v1/tasks/{done}',owner)
    assert call('GET',root+'/throughput',owner)['data']['totalDoneTasks']==3
    passed.append('first-completion throughput survives reopen/recomplete/delete exactly once')
    # Inclusive UTC dates, exclusive next midnight, preserve zero days.
    start=(now-timedelta(days=15)).replace(hour=0,minute=0,second=0)
    end=start+timedelta(days=2)
    for label,stamp in [('before',start-timedelta(seconds=1)),('first boundary',start),('last boundary',end-timedelta(seconds=1)),('after',end)]:
        task(label,'DONE',stamp-timedelta(days=1),stamp)
    days=call('GET',root+'/throughput?from='+start.date().isoformat()+'&to='+(end-timedelta(days=1)).date().isoformat(),owner)['data']
    assert days['totalDoneTasks']==2 and [p['doneTasks'] for p in days['points']]==[1,1]
    passed.append('UTC interval includes boundaries and excludes next midnight')
    # Completion history prevents deleting a Sprint even after its completed Task was removed.
    history_sprint=sprint(pid,'Historical',-26)
    history_task=task('History only','DONE',now-timedelta(days=2),now-timedelta(days=1),history_sprint)
    call('DELETE',f'/api/v1/tasks/{history_task}',owner)
    call('DELETE',f'/api/v1/sprints/{history_sprint}',owner,expected=409,label='Sprint completion history preserved after Task deletion')
    # Legacy SQL views now use UTC consistently too.
    age_days=float(sql(f'SELECT WorkItemAgeDays FROM dbo.vw_TaskFlow WHERE TaskId={active}'))
    assert abs(age_days-2)<0.02
    passed.append('legacy flow view uses UTC age consistently')
    call('PATCH',f'/api/v1/projects/{pid}/close',owner)
    call('GET',root+'/wip',owner,label='closed project metrics readable')
    call('DELETE',f'/api/v1/projects/{pid}',owner)
    call('GET',root+'/wip',owner,expected=404,label='deleted project metrics hidden')
    call('GET','/api/v1/projects/0/flow-metrics/wip',owner,expected=400,label='invalid project id')
    print(f'PASS ALL: {len(passed)} checks')


if __name__=='__main__':main()
