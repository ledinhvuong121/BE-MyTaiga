"""Run against disposable SQL/API setup from docs/testing-projects.md."""
import uuid
from projects_integration import call, sql, user, token_for_admin, passed


def main():
    _, manager = user()
    member_id, member = user()
    outsider_id, outsider = user()
    admin = token_for_admin()
    pid = call('POST', '/api/v1/projects', manager, {'projectName': 'Departments ' + uuid.uuid4().hex}, 201)['data']['projectId']
    other = call('POST', '/api/v1/projects', outsider, {'projectName': 'Other ' + uuid.uuid4().hex}, 201)['data']['projectId']
    root = f'/api/v1/projects/{pid}'
    path = root + '/departments'
    call('POST', root + '/members', manager, {'userId': member_id}, 201)
    call('GET', path, expected=401, label='anonymous departments denied')
    call('GET', path, outsider, expected=403, label='outsider departments denied')
    call('GET', path, admin, expected=403, label='admin departments denied')
    assert call('GET', path, member)['data']['total'] == 0
    for token, label in [(member, 'member'), (outsider, 'outsider'), (admin, 'admin')]:
        call('POST', path, token, {'departmentName': 'Invalid'}, 403, label + ' cannot create department')
    for data, label in [({'departmentName': '   '}, 'blank name'),
                        ({'departmentName': 'x' * 101}, 'long name'),
                        ({'departmentName': 'Valid', 'description': 'x' * 501}, 'long description')]:
        call('POST', path, manager, data, 400, label)
    dept = call('POST', path, manager, {'departmentName': 'Backend', 'description': 'Development'}, 201, 'create department')['data']['departmentId']
    dept2 = call('POST', path, manager, {'departmentName': 'Frontend'}, 201)['data']['departmentId']
    other_dept = call('POST', f'/api/v1/projects/{other}/departments', outsider, {'departmentName': 'Backend'}, 201)['data']['departmentId']
    call('POST', path, manager, {'departmentName': ' Backend '}, 409, 'duplicate normalized name')
    call('PUT', path + f'/{dept2}', manager, {'departmentName': 'Backend'}, 409, 'rename duplicate conflict')
    assert call('GET', path, manager)['data']['total'] == 2
    assert call('GET', path + '?pageSize=1&page=2', manager)['data']['items'][0]['departmentId'] == dept2
    passed.append('department pagination')
    call('GET', path + '?page=0', manager, expected=400, label='invalid page')
    call('GET', path + '?pageSize=101', manager, expected=400, label='invalid page size')
    call('PUT', path + f'/{other_dept}', manager, {'departmentName': 'Intrusion'}, 404, 'cross-project edit denied')
    call('DELETE', path + f'/{other_dept}', manager, expected=404, label='cross-project delete denied')
    assign = root + f'/members/{member_id}/department'
    call('PATCH', assign, member, {'departmentId': dept}, 403, 'member cannot assign self')
    call('PATCH', assign, manager, {'departmentId': other_dept}, 404, 'cross-project assignment denied')
    call('PATCH', assign, manager, {'departmentId': 0}, 400, 'invalid department id')
    call('PATCH', root + f'/members/{outsider_id}/department', manager, {'departmentId': dept}, 404, 'nonmember cannot receive department')
    call('PATCH', assign, manager, {'departmentId': dept}, label='assign member department')
    assert sql(f'SELECT DepartmentId FROM dbo.ProjectMember WHERE ProjectId={pid} AND UserId={member_id}') == str(dept)
    rows = call('GET', root + f'/members?departmentId={dept}', member)['data']
    assert rows['total'] == 1 and rows['items'][0]['departmentName'] == 'Backend'
    passed.append('assigned department visible in member filter')
    call('PATCH', assign, manager, {'departmentId': dept}, 409, 'repeat assignment conflict')
    call('DELETE', path + f'/{dept}', manager, expected=409, label='occupied department cannot delete')
    call('PATCH', assign, manager, {'departmentId': dept2}, label='transfer member department')
    call('DELETE', path + f'/{dept}', manager, label='delete vacated department')
    call('PATCH', assign, manager, {'departmentId': None}, label='unassign department')
    call('PUT', path + f'/{dept2}', manager, {'departmentName': 'Design', 'description': None}, label='update department')
    assert call('GET', path, member)['data']['items'][0]['departmentName'] == 'Design'
    for method, suffix, body in [('PUT', f'/{dept2}', {'departmentName': 'Attack'}), ('DELETE', f'/{dept2}', None)]:
        call(method, path + suffix, member, body, 403, 'member denied ' + method)
    call('DELETE', path + '/0', manager, expected=400, label='invalid delete id')
    call('DELETE', path + '/9223372036854775807', manager, expected=404, label='missing department')
    call('PATCH', root + '/close', manager)
    call('POST', path, manager, {'departmentName': 'Closed'}, 409, 'closed project create denied')
    call('PATCH', assign, manager, {'departmentId': dept2}, 409, 'closed project assign denied')
    call('DELETE', path + f'/{dept2}', manager, expected=409, label='closed project delete denied')
    call('GET', path, member, label='closed project remains readable')
    call('DELETE', root, manager)
    call('GET', path, member, expected=404, label='deleted project departments hidden')
    assert sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE ProjectId={pid} AND Action='DEPARTMENT_CREATED'") == '2'
    assert sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE ProjectId={pid} AND Action='MEMBER_DEPARTMENT_CHANGED'") == '3'
    assert sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE ProjectId={pid} AND Action='DEPARTMENT_UPDATED'") == '1'
    passed.append('audit logs exclude failed mutations')
    print(f'PASS ALL: {len(passed)} checks')


if __name__ == '__main__':
    main()
