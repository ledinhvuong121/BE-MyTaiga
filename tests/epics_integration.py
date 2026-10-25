"""Real Epic/PBI/Task link checks on disposable test database."""
import uuid
from projects_integration import call, sql, user, token_for_admin, passed


def main():
    owner_id, owner = user()
    member_id, member = user()
    _, outsider = user()
    admin = token_for_admin()
    pid = call('POST', '/api/v1/projects', owner, {'projectName': 'Epics ' + uuid.uuid4().hex}, 201)['data']['projectId']
    other = call('POST', '/api/v1/projects', outsider, {'projectName': 'Other epics ' + uuid.uuid4().hex}, 201)['data']['projectId']
    root = f'/api/v1/projects/{pid}'
    path = root + '/epics'
    call('POST', root + '/members', owner, {'userId': member_id}, 201)
    call('GET', path, expected=401, label='anonymous epic list denied')
    call('GET', path, outsider, expected=403, label='outsider epic list denied')
    call('POST', path, member, {'title': 'Denied'}, 403, 'member epic create denied')
    call('POST', path, admin, {'title': 'Denied'}, 403, 'admin epic create denied')
    for body, label in [({'title': ' '}, 'blank title'), ({'title': 'x' * 256}, 'long title'),
                        ({'title': 'x', 'priority': 0}, 'invalid priority'),
                        ({'title': 'x', 'status': 'BAD'}, 'invalid status')]:
        call('POST', path, owner, body, 400, label)
    eid = call('POST', path, owner, {'title': 'Epic one', 'priority': 1}, 201, 'create epic')['data']['epicId']
    second = call('POST', path, owner, {'title': 'Epic two'}, 201)['data']['epicId']
    foreign = call('POST', f'/api/v1/projects/{other}/epics', outsider, {'title': 'Other'}, 201)['data']['epicId']
    detail = f'/api/v1/epics/{eid}'
    assert call('GET', detail, member)['data']['title'] == 'Epic one'
    assert call('GET', path + '?keyword=one', member)['data']['total'] == 1
    assert len(call('GET', path + '?pageSize=1', owner)['data']['items']) == 1
    call('GET', path + '?page=0', owner, expected=400, label='invalid epic page')
    call('GET', path + '?status=BAD', owner, expected=400, label='invalid status filter')
    call('GET', detail, outsider, expected=403, label='outsider epic detail denied')
    call('PUT', detail, member, {'title': 'Denied'}, 403, 'member epic update denied')
    call('DELETE', detail, member, expected=403, label='member epic delete denied')
    call('PUT', detail, owner, {'title': 'Updated', 'status': 'IN_PROGRESS', 'priority': 2}, label='update epic')
    assert call('GET', path + '?status=IN_PROGRESS', member)['data']['total'] == 1
    empty = call('GET', detail + '/progress', member, label='empty progress')['data']
    assert empty['totalPbis'] == empty['totalTasks'] == empty['pbiProgressPercent'] == empty['taskProgressPercent'] == 0
    pbi = int(sql(f"INSERT dbo.ProductBacklogItem(EpicId,Title,Status) VALUES({eid},N'PBI done','DONE'); SELECT SCOPE_IDENTITY()"))
    sql(f"INSERT dbo.ProductBacklogItem(EpicId,Title) VALUES({eid},N'PBI new')")
    task = int(sql(f"INSERT dbo.Task(ProjectId,EpicId,ReporterId,Title) VALUES({pid},{eid},{owner_id},N'Task'); SELECT SCOPE_IDENTITY()"))
    progress = call('GET', detail + '/progress', member, label='progress with actual work')['data']
    assert progress['totalPbis'] == 2 and progress['completedPbis'] == 1 and progress['pbiProgressPercent'] == 50
    assert progress['totalTasks'] == 1 and progress['taskProgressPercent'] == 0
    call('DELETE', detail, owner, expected=409, label='linked epic deletion blocked')
    call('PATCH', f'/api/v1/pbis/{pbi}/epic', member, {'epicId': second}, 403, 'member pbi link denied')
    call('PATCH', f'/api/v1/pbis/{pbi}/epic', owner, {'epicId': foreign}, 404, 'cross-project pbi link denied')
    call('PATCH', f'/api/v1/tasks/{task}/epic', owner, {'epicId': foreign}, 404, 'cross-project task link denied')
    call('PATCH', f'/api/v1/pbis/{pbi}/epic', owner, {'epicId': second}, label='move pbi epic')
    call('PATCH', f'/api/v1/tasks/{task}/epic', owner, {'epicId': second}, label='link task epic')
    call('PATCH', f'/api/v1/tasks/{task}/epic', owner, {'epicId': second}, 409, 'duplicate task link')
    call('PATCH', f'/api/v1/pbis/{pbi}/epic', owner, {'epicId': second}, 409, 'duplicate pbi link')
    call('PATCH', f'/api/v1/tasks/{task}/epic', owner, {'epicId': 0}, 400, 'invalid link epic id')
    call('PATCH', '/api/v1/tasks/9223372036854775807/epic', owner, {'epicId': second}, 404, 'missing task link')
    call('PATCH', '/api/v1/pbis/9223372036854775807/epic', owner, {'epicId': second}, 404, 'missing pbi link')
    assert sql(f'SELECT EpicId FROM dbo.ProductBacklogItem WHERE PBIId={pbi}') == str(second)
    assert sql(f'SELECT EpicId FROM dbo.Task WHERE TaskId={task}') == str(second)
    passed.append('SQL links match successful HTTP mutations')
    remaining = int(sql(f'SELECT PBIId FROM dbo.ProductBacklogItem WHERE EpicId={eid}'))
    call('PATCH', f'/api/v1/pbis/{remaining}/epic', owner, {'epicId': second})
    call('DELETE', detail, owner, label='delete empty epic')
    call('GET', detail, member, expected=404, label='deleted epic missing')
    call('GET', '/api/v1/epics/0', owner, expected=400, label='invalid epic id')
    call('PATCH', root + '/close', owner)
    call('POST', path, owner, {'title': 'Closed'}, 409, 'closed project epic create denied')
    call('PATCH', f'/api/v1/tasks/{task}/epic', owner, {'epicId': second}, 409, 'closed project link denied')
    call('GET', f'/api/v1/epics/{second}/progress', member, label='closed project progress readable')
    call('DELETE', root, owner)
    call('GET', path, member, expected=404, label='deleted project epics hidden')
    call('GET', f'/api/v1/epics/{second}/progress', member, expected=404, label='deleted project progress hidden')
    assert sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE ProjectId={pid} AND Action='EPIC_CREATED'") == '2'
    assert sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE ProjectId={pid} AND Action='EPIC_LINKED'") == '3'
    passed.append('epic audit logs exclude failures')
    print(f'PASS ALL: {len(passed)} checks')


if __name__ == '__main__':
    main()
