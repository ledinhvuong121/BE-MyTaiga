import uuid
from projects_integration import call,sql,user,token_for_admin,passed

def main():
    manager_id,manager=user();author_id,author=user();assignee_id,assignee=user();outsider_id,outsider=user();admin=token_for_admin()
    project=call('POST','/api/v1/projects',manager,{'projectName':'Comments '+uuid.uuid4().hex},201)['data']['projectId']
    for uid in [author_id,assignee_id]:call('POST',f'/api/v1/projects/{project}/members',manager,{'userId':uid},201)
    task=call('POST',f'/api/v1/projects/{project}/tasks',manager,{'title':'Discuss','assigneeId':assignee_id},201)['data']['taskId']
    path=f'/api/v1/tasks/{task}/comments'
    call('POST',path,body={'content':'Hi'},expected=401,label='anonymous comment denied')
    call('POST',path,outsider,{'content':'Hi'},403,'outsider comment denied')
    call('GET',path,admin,expected=403,label='Admin has no comment bypass')
    for content,label in [(' ','blank'),('x'*5001,'too long'),(f'@[Outsider](user:{outsider_id})','foreign mention'),('@[Invalid](user:9223372036854775808)','overflow mention')]:
        call('POST',path,author,{'content':content},400,label)
    call('POST',path,author,{'content':' '.join(f'@[User](user:{i})' for i in range(1,22))},400,'more than 20 mentions')
    row=call('POST',path,author,{'content':f'Hello @[Worker](user:{assignee_id}) and @[Me](user:{author_id})','userId':outsider_id},201,'create mentioned comment')['data']
    cid=row['commentId'];detail=f'/api/v1/comments/{cid}'
    assert row['userId']==author_id and row['mentionedUserIds']==sorted([assignee_id,author_id])
    assert sql(f"SELECT COUNT(*) FROM dbo.Notification WHERE RefType='COMMENT' AND RefId={cid} AND UserId={assignee_id}")=='1'
    assert sql(f"SELECT COUNT(*) FROM dbo.Notification WHERE RefType='COMMENT' AND RefId={cid} AND UserId={author_id}")=='0'
    passed.append('assignee + mention deduplicated; author never notified')
    call('PUT',detail,manager,{'content':'Manager edit'},403,'Manager cannot edit other author')
    call('DELETE',detail,assignee,expected=403,label='Member cannot delete other author')
    call('PUT',detail,author,{'content':f'Edited @[Worker](user:{assignee_id})'},label='author edits own comment')
    assert sql(f"SELECT COUNT(*) FROM dbo.Notification WHERE RefType='COMMENT' AND RefId={cid} AND UserId={assignee_id}")=='1'
    call('PUT',detail,author,{'content':f'Added @[Manager](user:{manager_id})'},label='new mention on edit notifies once')
    assert sql(f"SELECT COUNT(*) FROM dbo.Notification WHERE RefType='COMMENT' AND RefId={cid} AND UserId={manager_id}")=='1'
    second=call('POST',path,author,{'content':'Plain @name, no structured ID'},201,'plain comment still notifies assignee')['data']['commentId']
    assert sql(f"SELECT COUNT(*) FROM dbo.Notification WHERE RefType='COMMENT' AND RefId={second} AND UserId={assignee_id}")=='1'
    assert call('GET',path,assignee)['data']['total']==2
    assert len(call('GET',path+'?pageSize=1',assignee)['data']['items'])==1
    call('GET',path+'?page=0',author,expected=400,label='invalid page')
    call('GET',path+'?pageSize=101',author,expected=400,label='oversized page')
    call('GET',path+'?page=2147483647',author,expected=400,label='page overflow')
    call('GET',detail,outsider,expected=403,label='outsider comment detail denied')
    # System setting can tighten the ceiling without exceeding hard limit.
    sql("UPDATE dbo.SystemConfig SET ConfigValue='5' WHERE ConfigKey='MAX_COMMENT_LENGTH'")
    try:call('POST',path,author,{'content':'123456'},400,'configured comment ceiling')
    finally:sql("UPDATE dbo.SystemConfig SET ConfigValue='5000' WHERE ConfigKey='MAX_COMMENT_LENGTH'")
    call('DELETE',detail,author,label='author deletes own comment')
    call('GET',detail,author,expected=404,label='deleted comment missing')
    assert sql(f'SELECT COUNT(*) FROM dbo.Task WHERE TaskId={task}')=='1'
    call('DELETE',f'/api/v1/projects/{project}/members/{author_id}',manager)
    call('GET',f'/api/v1/comments/{second}',author,expected=403,label='departed author loses access')
    call('DELETE',f'/api/v1/comments/{second}',author,expected=403,label='departed author cannot delete')
    call('PATCH',f'/api/v1/projects/{project}/close',manager)
    call('POST',path,assignee,{'content':'Closed'},409,'closed project comment denied')
    call('GET',path,assignee,label='closed project comments readable')
    call('DELETE',f'/api/v1/projects/{project}',manager)
    call('GET',path,assignee,expected=404,label='deleted project comments hidden')
    call('GET','/api/v1/comments/0',manager,expected=400,label='invalid comment id')
    print(f'PASS ALL: {len(passed)} checks')

if __name__=='__main__':main()
