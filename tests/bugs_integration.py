"""Bug lifecycle, permissions and detail integration checks on disposable SQL."""
from datetime import datetime,timedelta,timezone
import uuid
from projects_integration import call,sql,user,token_for_admin,passed


def main():
    owner_id,owner=user();reporter_id,reporter=user();worker_id,worker=user();outsider_id,outsider=user();locked_id,_=user();admin=token_for_admin()
    project=call('POST','/api/v1/projects',owner,{'projectName':'Bugs '+uuid.uuid4().hex},201)['data']['projectId']
    other=call('POST','/api/v1/projects',outsider,{'projectName':'Other bugs '+uuid.uuid4().hex},201)['data']['projectId']
    for uid in [reporter_id,worker_id,locked_id]:call('POST',f'/api/v1/projects/{project}/members',owner,{'userId':uid},201)
    sql(f'UPDATE dbo.[User] SET Status=0 WHERE UserId={locked_id}')
    task=call('POST',f'/api/v1/projects/{project}/tasks',reporter,{'title':'Affected task'},201)['data']['taskId']
    foreign_task=call('POST',f'/api/v1/projects/{other}/tasks',outsider,{'title':'Foreign task'},201)['data']['taskId']
    path='/api/v1/bugs';listing=f'/api/v1/projects/{project}/bugs'
    body={'taskId':task,'title':'Cannot save','description':'1. Open form. 2. Save. Error.','severity':'MAJOR','priority':'HIGH','environment':'Windows'}
    call('POST',path,body=body,expected=401,label='anonymous Bug create denied')
    call('POST',path,outsider,body,403,'outsider Bug create denied')
    call('POST',path,admin,body,403,'Admin no Bug bypass')
    call('POST',path,reporter,{**body,'taskId':foreign_task},403,'cross-project Bug creation denied')
    call('POST',path,reporter,{**body,'taskId':9223372036854775807},404,'missing Task')
    for changes,label in [({'taskId':0},'invalid Task id'),({'title':' '},'blank title'),({'description':' '},'blank reproduction'),
                           ({'title':'x'*256},'long title'),({'description':'x'*10001},'long description'),
                           ({'environment':'x'*501},'long environment'),({'severity':'HIGH'},'invalid severity'),({'priority':'MAJOR'},'invalid priority')]:
        call('POST',path,reporter,{**body,**changes},400,label)
    bug=call('POST',path,reporter,{**body,'reporterId':outsider_id,'status':'CLOSED'},201,'Member reports Bug')['data']
    bid=bug['bugId'];detail=path+f'/{bid}'
    assert bug['reporterId']==reporter_id and bug['status']=='OPEN' and bug['resolvedAt'] is None
    assert bug['createdAt'].endswith('Z')
    assert sql(f"SELECT COUNT(*) FROM dbo.Notification WHERE UserId={owner_id} AND RefType='BUG' AND RefId={bid} AND Type='BUG_CREATED'")=='1'
    passed.append('Bug notifies Manager once; caller cannot forge reporter/status')
    second=call('POST',f'/api/v1/tasks/{task}/bugs',worker,{k:v for k,v in body.items() if k!='taskId'},201,'Task-scoped create alias')['data']['bugId']
    call('GET',detail,outsider,expected=403,label='outsider Bug detail denied')
    call('GET',detail,admin,expected=403,label='Admin Bug detail denied')
    call('GET',listing,outsider,expected=403,label='outsider Bug list denied')
    call('PUT',detail,worker,{k:v for k,v in {**body,'title':'Updated'}.items() if k!='taskId'},label='Member edits Bug')
    call('PATCH',detail+'/priority',worker,{'priority':'CRITICAL'},label='Member sets Bug priority')
    call('PATCH',detail+'/priority',worker,{'priority':'BAD'},400,'invalid priority patch')
    call('PATCH',detail+'/assignee',reporter,{'assigneeId':outsider_id},409,'foreign Bug assignee denied')
    call('PATCH',detail+'/assignee',reporter,{'assigneeId':locked_id},409,'locked Bug assignee denied')
    call('PATCH',detail+'/assignee',reporter,{'assigneeId':worker_id},label='Member assigns Bug within project')
    call('PATCH',detail+'/assignee',reporter,{'assigneeId':worker_id},409,'repeat Bug assignment conflict')
    assert sql(f"SELECT COUNT(*) FROM dbo.Notification WHERE UserId={worker_id} AND RefId={bid} AND RefType='BUG' AND Type='BUG_ASSIGNED'")=='1'
    assert call('GET',listing+f'?taskId={task}&assigneeId={worker_id}&severity=MAJOR&priority=CRITICAL&keyword=Updated',worker)['data']['total']==1
    for query,label in [('status=BAD','invalid status filter'),('severity=HIGH','invalid severity filter'),('priority=MAJOR','invalid priority filter'),
                         ('page=0','invalid page'),('pageSize=101','invalid page size'),('page=2147483647','pagination overflow')]:
        call('GET',listing+'?'+query,worker,expected=400,label=label)
    call('GET',listing+f'?taskId={foreign_task}',worker,expected=404,label='foreign Task filter denied')
    call('GET',listing+f'?assigneeId={outsider_id}',worker,expected=404,label='foreign assignee filter denied')
    call('PATCH',detail+'/status',worker,{'status':'BAD'},400,'invalid workflow enum')
    call('PATCH',detail+'/status',worker,{'status':'RESOLVED'},409,'cannot skip directly to resolved')
    call('POST',detail+'/close',worker,expected=409,label='cannot close OPEN Bug directly')
    def status(value,label):return call('PATCH',detail+'/status',worker,{'status':value},label=label)['data']
    status('IN_PROGRESS','start Bug work')
    resolved=status('RESOLVED','resolve Bug')
    assert resolved['resolvedAt'] is not None
    moment=datetime.fromisoformat(resolved['resolvedAt'].replace('Z','+00:00'))
    assert abs((datetime.now(timezone.utc)-moment).total_seconds())<30
    closed=call('POST',detail+'/close',reporter,label='close resolved Bug')['data']
    assert closed['resolvedAt']==resolved['resolvedAt'] and closed['status']=='CLOSED'
    call('POST',detail+'/close',reporter,expected=409,label='repeat close conflict')
    reopened=status('REOPENED','reopen closed Bug')
    assert reopened['resolvedAt'] is None
    status('IN_PROGRESS','restart reopened Bug')
    # Detail shares Task comments/files because the schema attaches them to Task, never exposes server paths.
    sql(f"INSERT dbo.Comment(TaskId,UserId,Content) VALUES({task},{reporter_id},N'Reproduction detail')")
    sql(f"INSERT dbo.Attachment(TaskId,FileName,FilePath,FileSize,UploadedBy) VALUES({task},N'evidence.png',N'private-server-path',10,{reporter_id})")
    data=call('GET',detail,owner,label='Bug detail includes bounded comments/files/history')['data']
    assert data['taskComments']['total']==1 and data['taskAttachments']['total']==1 and data['history']['total']>=7
    assert data['taskAttachments']['items'][0]['fileName']=='evidence.png'
    assert 'filePath' not in data['taskAttachments']['items'][0]
    passed.append('Bug evidence metadata never leaks server file paths')
    # Losing project membership revokes Bug access and prevents processing with obsolete assignment.
    call('DELETE',f'/api/v1/projects/{project}/members/{worker_id}',owner)
    call('GET',detail,worker,expected=403,label='removed member loses Bug access')
    call('PATCH',detail+'/status',owner,{'status':'RESOLVED'},409,'must reassign Bug from departed worker')
    call('PATCH',detail+'/assignee',owner,{'assigneeId':None},label='clear obsolete Bug assignment')
    call('PATCH',detail+'/status',owner,{'status':'RESOLVED'},label='resolve after unassigning departed worker')
    call('PATCH',detail+'/status',owner,{'status':'REOPENED'},label='reopen resolved Bug')
    call('DELETE',f'/api/v1/projects/{project}/members/{reporter_id}',owner)
    call('PUT',detail,owner,{k:v for k,v in {**body,'title':'Historical reporter retained'}.items() if k!='taskId'},label='keep former reporter in Bug history')
    # A newly discovered Bug can be reported against a completed Sprint's Task.
    today=datetime.now(timezone.utc).date()
    sprint=call('POST','/api/v1/sprints',owner,{'projectId':project,'sprintName':'Closed for Bug','startDate':today.isoformat(),'endDate':(today+timedelta(days=5)).isoformat()},201)['data']['sprintId']
    sprint_task=call('POST',f'/api/v1/projects/{project}/tasks',owner,{'title':'Shipped','sprintId':sprint},201)['data']['taskId']
    epic=call('POST',f'/api/v1/projects/{project}/epics',owner,{'title':'Sprint Epic'},201)['data']['epicId']
    pbi=call('POST','/api/v1/product-backlog-items',owner,{'epicId':epic,'title':'PBI'},201)['data']['pbiId']
    call('POST',f'/api/v1/sprints/{sprint}/backlog-items',owner,{'pbiIds':[pbi]})
    call('POST',f'/api/v1/sprints/{sprint}/start',owner);call('POST',f'/api/v1/sprints/{sprint}/close',owner)
    call('POST',path,owner,{**body,'taskId':sprint_task},201,'report newly found Bug after Sprint closure')
    call('DELETE',f'/api/v1/tasks/{task}',owner,expected=409,label='Task deletion blocked while Bug/evidence exists')
    call('DELETE',path+f'/{second}',owner,label='delete Bug without cascading Task evidence')
    call('GET',path+f'/{second}',owner,expected=404,label='deleted Bug missing')
    assert sql(f'SELECT COUNT(*) FROM dbo.Comment WHERE TaskId={task}')=='1'
    assert sql(f'SELECT COUNT(*) FROM dbo.Attachment WHERE TaskId={task}')=='1'
    passed.append('deleting Bug preserves Task/comments/files')
    call('GET',path+'/0',owner,expected=400,label='invalid Bug id')
    call('GET',path+'/9223372036854775807',owner,expected=404,label='missing Bug')
    failed_before=sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE EntityType='BUG' AND EntityId={bid} AND ProjectId={project}")
    call('PATCH',detail+'/status',owner,{'status':'CLOSED'},409,'invalid transition rolls back')
    assert sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE EntityType='BUG' AND EntityId={bid} AND ProjectId={project}")==failed_before
    passed.append('invalid workflow has no audit or timestamp side effects')
    call('PATCH',f'/api/v1/projects/{project}/close',owner)
    call('POST',path,owner,body,409,'closed project Bug create denied')
    call('PATCH',detail+'/priority',owner,{'priority':'HIGH'},409,'closed project Bug mutation denied')
    call('GET',detail,owner,label='closed project Bug readable')
    call('DELETE',f'/api/v1/projects/{project}',owner)
    call('GET',detail,owner,expected=404,label='deleted project Bug hidden')
    print(f'PASS ALL: {len(passed)} checks')


if __name__=='__main__':main()
