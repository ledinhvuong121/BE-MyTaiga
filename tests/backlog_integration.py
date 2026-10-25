"""Product Backlog CRUD/ordering tests on disposable SQL Server database."""
import uuid
from projects_integration import call, sql, user, token_for_admin, passed


def main():
    _, owner = user()
    member_id, member = user()
    _, outsider = user()
    admin = token_for_admin()
    project = call('POST', '/api/v1/projects', owner, {'projectName': 'Backlog ' + uuid.uuid4().hex}, 201)['data']['projectId']
    foreign_project = call('POST', '/api/v1/projects', outsider, {'projectName': 'Foreign ' + uuid.uuid4().hex}, 201)['data']['projectId']
    epic = call('POST', f'/api/v1/projects/{project}/epics', owner, {'title': 'Backlog Epic'}, 201)['data']['epicId']
    foreign = call('POST', f'/api/v1/projects/{foreign_project}/epics', outsider, {'title': 'Foreign Epic'}, 201)['data']['epicId']
    call('POST', f'/api/v1/projects/{project}/members', owner, {'userId': member_id}, 201)
    path = '/api/v1/product-backlog-items'
    listing = path + f'?projectId={project}'
    body = {'epicId': epic, 'title': 'Backlog first', 'storyPoint': 5, 'status': 'READY'}
    call('POST', path, body=body, expected=401, label='anonymous backlog create denied')
    call('GET', listing, outsider, expected=403, label='outsider backlog denied')
    call('POST', path, member, body, 403, 'member backlog create denied')
    call('POST', path, admin, body, 403, 'admin backlog create denied')
    call('GET', path, owner, expected=400, label='project scope required')
    for change, label in [({'title': ' '}, 'blank title'), ({'title': 'x'*256}, 'long title'),
                           ({'storyPoint': -1}, 'negative story points'), ({'priority': 0}, 'invalid priority'),
                           ({'status': 'BAD'}, 'invalid status'), ({'epicId': 0}, 'invalid epic')]:
        call('POST', path, owner, {**body, **change}, 400, label)
    call('POST', path, owner, {**body, 'status': 'IN_SPRINT'}, 409, 'cannot fake sprint membership')
    first = call('POST', path, owner, body, 201, 'create PBI')['data']['pbiId']
    second = call('POST', path, owner, {**body, 'title': 'Backlog second'}, 201)['data']['pbiId']
    third = call('POST', path, owner, {**body, 'title': 'Backlog third'}, 201)['data']['pbiId']
    foreign_pbi = call('POST', path, outsider, {**body, 'epicId': foreign}, 201)['data']['pbiId']
    detail = path + f'/{first}'
    assert call('GET', detail, member)['data']['storyPoint'] == 5
    assert call('GET', listing + '&status=READY&keyword=first', member)['data']['total'] == 1
    call('GET', listing + '&status=BAD', owner, expected=400, label='invalid status filter')
    call('GET', listing + f'&epicId={foreign}', owner, expected=404, label='foreign epic filter denied')
    call('GET', listing + '&pageSize=101', owner, expected=400, label='page size validation')
    assert len(call('GET', listing + '&pageSize=1&page=2', owner)['data']['items']) == 1
    call('GET', detail, outsider, expected=403, label='outsider detail denied')
    call('PUT', detail, member, body, 403, 'member update denied')
    call('DELETE', detail, member, expected=403, label='member delete denied')
    call('PUT', detail, owner, {**body, 'epicId': foreign}, 404, 'cannot move PBI to foreign project')
    call('PUT', detail, owner, {**body, 'title': 'Updated PBI', 'status': 'DONE'}, label='update PBI')
    assert call('GET', detail, owner)['data']['status'] == 'DONE'
    call('PATCH', detail + '/priority', owner, {'priority': 1}, label='change priority')
    call('PATCH', detail + '/priority', member, {'priority': 1}, 403, 'member priority denied')
    call('PATCH', detail + '/priority', owner, {'priority': -1}, 400, 'invalid priority change')
    for ids, status, label in [([first, first, third], 400, 'duplicate order ids'),
                                ([0, second, third], 400, 'invalid order ids'),
                                ([first, second], 409, 'incomplete ordering'),
                                ([first, second, foreign_pbi], 409, 'foreign item ordering')]:
        call('PUT', path + '/order', owner, {'epicId': epic, 'pbiIds': ids}, status, label)
    call('PUT', path + '/order', member, {'epicId': epic, 'pbiIds': [third, first, second]}, 403, 'member order denied')
    call('PUT', path + '/order', owner, {'epicId': epic, 'pbiIds': [third, first, second]}, label='reorder entire epic')
    ids = [x['pbiId'] for x in call('GET', listing, member)['data']['items']]
    assert ids == [third, first, second]
    assert sql(f'SELECT Priority FROM dbo.ProductBacklogItem WHERE PBIId={third}') == '1'
    passed.append('persisted ordering matches list order')
    sprint = int(sql(f"INSERT dbo.Sprint(ProjectId,SprintName,StartDate,EndDate) VALUES({project},N'Test Sprint','2026-10-25','2026-11-07'); SELECT SCOPE_IDENTITY()"))
    sql(f"INSERT dbo.SprintBacklogItem(SprintId,PBIId) VALUES({sprint},{second}); UPDATE dbo.ProductBacklogItem SET Status='IN_SPRINT' WHERE PBIId={second}")
    call('DELETE', path + f'/{second}', owner, expected=409, label='cannot delete sprint-linked PBI')
    call('PUT', path + f'/{second}', owner, body, 409, 'cannot return sprint-linked PBI to ready')
    call('PUT', path + f'/{second}', owner, {**body, 'status': 'IN_SPRINT'}, label='update sprint-linked PBI')
    call('DELETE', detail, owner, label='delete unlinked PBI')
    call('GET', detail, owner, expected=404, label='deleted PBI missing')
    call('GET', path + '/0', owner, expected=400, label='invalid PBI id')
    call('GET', path + '/9223372036854775807', owner, expected=404, label='missing PBI')
    call('PATCH', f'/api/v1/projects/{project}/close', owner)
    call('POST', path, owner, body, 409, 'closed project create denied')
    call('PATCH', path + f'/{third}/priority', owner, {'priority': 2}, 409, 'closed project priority denied')
    call('GET', listing, member, label='closed project backlog readable')
    call('DELETE', f'/api/v1/projects/{project}', owner)
    call('GET', listing, member, expected=404, label='deleted project backlog hidden')
    assert sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE ProjectId={project} AND Action='PBI_CREATED'") == '3'
    assert sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE ProjectId={project} AND Action='PBI_REORDERED'") == '1'
    passed.append('failed mutations do not create audit logs')
    print(f'PASS ALL: {len(passed)} checks')


if __name__ == '__main__':
    main()
