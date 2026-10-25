"""Run after isolated database/API setup from docs/testing-projects.md."""
import concurrent.futures
import uuid
from projects_integration import call, sql, user, token_for_admin, passed


def main():
    owner_id, owner = user()
    member_id, member = user()
    outsider_id, outsider = user()
    locked_id, _ = user()
    sql(f'UPDATE dbo.[User] SET Status=0 WHERE UserId={locked_id}')
    admin = token_for_admin()
    admin_id = int(sql("SELECT UserId FROM dbo.[User] WHERE Email='admin@scrumban.local'"))
    pid = call('POST', '/api/v1/projects', owner, {'projectName': 'Members ' + uuid.uuid4().hex}, 201)['data']['projectId']
    root = f'/api/v1/projects/{pid}'
    path = root + '/members'
    call('GET', path, expected=401, label='anonymous list denied')
    call('GET', path, outsider, expected=403, label='outsider list denied')
    call('GET', path, admin, expected=403, label='admin list denied')
    call('POST', path, outsider, {'userId': member_id}, 403, 'outsider add denied')
    call('POST', path, admin, {'userId': member_id}, 403, 'admin add denied')
    call('POST', path, owner, {'userId': 0}, 400, 'invalid user id')
    call('POST', path, owner, {'userId': 9223372036854775807}, 404, 'missing user')
    call('POST', path, owner, {'userId': admin_id}, 400, 'cannot add system admin')
    call('POST', path, owner, {'userId': locked_id}, 409, 'cannot add locked user')
    call('DELETE', path + f'/{owner_id}', owner, expected=409, label='cannot remove last manager')
    call('POST', path, owner, {'userId': member_id, 'role': 'MANAGER'}, 201, 'add defaults to MEMBER')
    rows = call('GET', path, member)['data']
    assert rows['total'] == 2
    assert next(x for x in rows['items'] if x['userId'] == member_id)['projectRole'] == 'MEMBER'
    assert not any('passwordHash' in x or 'tokenVersion' in x for x in rows['items'])
    passed.append('list includes manager/member without credentials')
    call('POST', path, owner, {'userId': member_id}, 409, 'duplicate membership')
    call('POST', path, member, {'userId': outsider_id}, 403, 'member cannot add')
    call('DELETE', path + f'/{owner_id}', member, expected=403, label='member cannot remove manager')
    call('PATCH', path + f'/{member_id}/role', member, {'role': 'MANAGER'}, 403, 'member cannot promote self')
    call('PATCH', path + f'/{member_id}/role', owner, {'role': 'ADMIN'}, 400, 'reject system role assignment')
    call('PATCH', path + f'/{member_id}/role', owner, {'role': 'MEMBER'}, 400, 'no unsupported downgrade')
    call('GET', path + '?pageSize=101', owner, expected=400, label='member page size validation')
    call('GET', path + '?page=2147483647', owner, expected=400, label='member pagination overflow')
    call('GET', path + '?departmentId=0', owner, expected=400, label='invalid department filter')
    call('GET', path + '?departmentId=9223372036854775807', owner, expected=404, label='missing department filter')
    department_id = int(sql(f"INSERT dbo.Department(ProjectId,DepartmentName) VALUES({pid},N'Backend'); SELECT SCOPE_IDENTITY()"))
    sql(f'UPDATE dbo.ProjectMember SET DepartmentId={department_id} WHERE ProjectId={pid} AND UserId={member_id}')
    assert call('GET', path + f'?departmentId={department_id}', owner)['data']['total'] == 1
    assert call('GET', path + '?keyword=notfound-xyz', owner)['data']['total'] == 0
    assert len(call('GET', path + '?pageSize=1', owner)['data']['items']) == 1
    passed.append('filter department/name and pagination')
    sql(f"INSERT dbo.Task(ProjectId,ReporterId,AssigneeId,Title) VALUES({pid},{owner_id},{member_id},N'Open assignment')")
    call('DELETE', path + f'/{member_id}', owner, expected=409, label='unfinished assignment blocks removal')
    sql(f"UPDATE dbo.Task SET Status='DONE' WHERE ProjectId={pid}")
    call('DELETE', path + f'/{member_id}', owner, label='remove member after work completed')
    call('GET', path, member, expected=403, label='removed member immediately loses access')
    call('DELETE', path + f'/{member_id}', owner, expected=404, label='missing member removal')
    call('POST', path, owner, {'userId': member_id}, 201)
    call('PATCH', path + f'/{member_id}/role', owner, {'role': 'MANAGER'}, label='promote member to manager')
    call('PATCH', path + f'/{member_id}/role', owner, {'role': 'MANAGER'}, 409, 'repeat promotion conflict')
    call('PUT', root, member, {'projectName': 'Promoted ' + uuid.uuid4().hex}, label='promoted manager can update project')
    # Two managers attempting to remove themselves cannot leave a managerless project.
    def remove_self(pair):
        uid, token = pair
        try:
            call('DELETE', path + f'/{uid}', token)
            return 200
        except AssertionError as error:
            if 'got 409:' in str(error):
                return 409
            raise
    with concurrent.futures.ThreadPoolExecutor(2) as pool:
        assert sorted(pool.map(remove_self, [(owner_id, owner), (member_id, member)])) == [200, 409]
    assert sql(f"SELECT COUNT(*) FROM dbo.ProjectMember m JOIN dbo.Role r ON r.RoleId=m.RoleId WHERE ProjectId={pid} AND r.RoleName='MANAGER'") == '1'
    passed.append('concurrent removals preserve at least one manager')
    remaining_id = int(sql(f'SELECT UserId FROM dbo.ProjectMember WHERE ProjectId={pid}'))
    manager = owner if remaining_id == owner_id else member
    call('PATCH', root + '/close', manager)
    call('POST', path, manager, {'userId': outsider_id}, 409, 'closed project add denied')
    call('DELETE', path + f'/{remaining_id}', manager, expected=409, label='closed project remove denied')
    call('PATCH', path + f'/{remaining_id}/role', manager, {'role': 'MANAGER'}, 409, 'closed project promotion denied')
    call('DELETE', root, manager)
    call('GET', path, manager, expected=404, label='deleted project members hidden')
    call('POST', path, manager, {'userId': outsider_id}, 404, 'deleted project add denied')
    call('POST', '/api/v1/projects/0/members', owner, {'userId': outsider_id}, 400, 'invalid project id')
    call('GET', '/api/v1/projects/9223372036854775807/members', owner, expected=404, label='missing project')
    assert int(sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE ProjectId={pid} AND Action='MEMBER_ADDED'")) == 2
    assert int(sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE ProjectId={pid} AND Action='MEMBER_PROMOTED'")) == 1
    passed.append('membership logs committed exactly once per successful mutation')
    print(f'PASS ALL: {len(passed)} checks')


if __name__ == '__main__':
    main()
