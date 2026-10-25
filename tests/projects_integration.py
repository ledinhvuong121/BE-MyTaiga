"""Real HTTP + SQL Server checks. Use ONLY a disposable MyTaiga_*Checks_* database.

Start the API against that database with the matching test-only JWT settings.
See docs/testing-projects.md for setup. No production credentials are used.
"""
import base64
import concurrent.futures
import hashlib
import hmac
import json
import os
import subprocess
import time
import urllib.error
import urllib.request
import uuid

BASE = os.environ.get('TEST_API_URL', 'http://127.0.0.1:5159')
DATABASE = os.environ.get('TEST_DATABASE', 'MyTaiga_ProjectChecks_20261025')
SERVER = os.environ.get('TEST_SQL_SERVER', r'.\SQLEXPRESS')
assert DATABASE.startswith('MyTaiga_') and 'Checks_' in DATABASE, 'Disposable test database required'
KEY = os.environ.get('TEST_JWT_KEY', 'integration-test-only-key-not-for-production-20261025')
ISSUER = os.environ.get('TEST_JWT_ISSUER', 'MyTaigaIntegration')
passed = []


def sql(statement):
    result = subprocess.run(['sqlcmd', '-S', SERVER, '-E', '-C', '-I', '-b', '-d', DATABASE,
                             '-h', '-1', '-W', '-Q', 'SET NOCOUNT ON; ' + statement],
                            capture_output=True, text=True, check=True)
    return result.stdout.strip()


def call(method, path, token=None, body=None, expected=200, label=None):
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        response = urllib.request.urlopen(request, timeout=30)
    except urllib.error.HTTPError as error:
        response = error
    raw = response.read().decode()
    assert response.code == expected, f'{label or path}: expected {expected}, got {response.code}: {raw[:600]}'
    if label:
        passed.append(label)
        print('PASS', label, flush=True)
    return json.loads(raw) if raw else None


def token_for_admin():
    def encode(value):
        return base64.urlsafe_b64encode(json.dumps(value).encode()).rstrip(b'=')
    uid = int(sql("SELECT UserId FROM dbo.[User] WHERE Email='admin@scrumban.local'"))
    version = int(sql(f'SELECT TokenVersion FROM dbo.[User] WHERE UserId={uid}'))
    content = encode({'alg': 'HS256', 'typ': 'JWT'}) + b'.' + encode({
        'sub': str(uid), 'role': 'ADMIN', 'token_version': str(version),
        'iss': ISSUER, 'aud': ISSUER, 'exp': int(time.time()) + 1200})
    return (content + b'.' + base64.urlsafe_b64encode(hmac.new(KEY.encode(), content, hashlib.sha256).digest()).rstrip(b'=')).decode()


def user():
    email = f'project-check-{uuid.uuid4().hex}@example.com'
    password = 'Integration!' + uuid.uuid4().hex
    registered = call('POST', '/api/v1/auth/register', body={
        'fullName': 'Project integration user', 'email': email,
        'password': password, 'confirmPassword': password}, expected=201)
    login = call('POST', '/api/v1/auth/login', body={'email': email, 'password': password})
    return registered['data']['userId'], login['data']['accessToken']


def main():
    # Fail before any mutation if API uses another database/JWT configuration.
    admin = token_for_admin()
    call('GET', '/api/v1/users/me', admin, label='test database/JWT match')
    owner_id, owner = user()
    member_id, member = user()
    _, outsider = user()
    path = '/api/v1/projects'
    name = 'Dự án kiểm thử ' + uuid.uuid4().hex[:10]
    body = {'projectName': name, 'description': 'Tiếng Việt', 'startDate': '2026-10-25', 'endDate': '2026-11-25'}
    call('GET', path, expected=401, label='anonymous list denied')
    call('POST', path, body=body, expected=401, label='anonymous create denied')
    call('POST', path, admin, body, 403, 'admin cannot create')
    for invalid, label in [({'projectName': '   '}, 'blank name'),
                           ({'projectName': 'x' * 201}, 'long name'),
                           ({'projectName': 'x', 'description': 'x' * 10001}, 'long description'),
                           ({'projectName': 'x', 'startDate': '2026-11-01', 'endDate': '2026-10-01'}, 'reversed dates'),
                           ({'projectName': 'x', 'startDate': 'bad'}, 'invalid date')]:
        call('POST', path, owner, invalid, 400, label)
    created = call('POST', path, owner, {**body, 'createdBy': member_id, 'status': 'DELETED'}, 201, 'create project')
    project = created['data']
    pid = project['projectId']
    assert project['createdBy'] == owner_id and project['status'] == 'ACTIVE' and project['projectRole'] == 'MANAGER'
    assert project['createdAt'].endswith('Z')
    assert sql(f'SELECT COUNT(*) FROM dbo.BoardColumn WHERE ProjectId={pid}') == '4'
    assert sql(f"SELECT COUNT(*) FROM dbo.ProjectMember m JOIN dbo.Role r ON r.RoleId=m.RoleId WHERE m.ProjectId={pid} AND m.UserId={owner_id} AND r.RoleName='MANAGER'") == '1'
    assert sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE ProjectId={pid} AND Action='PROJECT_CREATED'") == '1'
    passed.append('SQL trigger creates four columns, manager and exactly one log')
    call('POST', path, owner, body, 409, 'duplicate creator/name conflict')
    call('GET', path + f'/{pid}', outsider, expected=403, label='outsider cannot read')
    call('GET', path + f'/{pid}', admin, expected=403, label='admin has no project bypass')
    assert call('GET', path, outsider)['data']['total'] == 0
    assert call('GET', path + '?keyword=' + urllib.parse.quote(name), owner)['data']['total'] == 1
    call('GET', path + '?page=0', owner, expected=400, label='invalid page')
    call('GET', path + '?pageSize=101', owner, expected=400, label='oversized page')
    call('GET', path + '?page=2147483647', owner, expected=400, label='pagination overflow')
    call('GET', path + '?status=DELETED', owner, expected=400, label='deleted filter denied')
    call('GET', path + '/0', owner, expected=400, label='invalid project id')
    call('GET', path + '/9223372036854775807', owner, expected=404, label='missing project')
    sql(f"INSERT dbo.ProjectMember(ProjectId,UserId,RoleId) SELECT {pid},{member_id},RoleId FROM dbo.Role WHERE RoleName='MEMBER' AND Scope='PROJECT'")
    assert call('GET', path + f'/{pid}', member)['data']['projectRole'] == 'MEMBER'
    assert call('GET', path, member)['data']['total'] == 1
    passed.append('member reads project and sees it in own list')
    for method, suffix, payload in [('PUT', '', body), ('PATCH', '/close', None), ('DELETE', '', None)]:
        call(method, path + f'/{pid}' + suffix, member, payload, 403, f'member denied {method}')
        call(method, path + f'/{pid}' + suffix, outsider, payload, 403, f'outsider denied {method}')
        call(method, path + f'/{pid}' + suffix, None, payload, 401, f'anonymous denied {method}')
    call('PUT', path + f'/{pid}', owner, {**body, 'projectName': 'Updated ' + name}, label='manager update')
    assert call('GET', path + f'/{pid}', owner)['data']['projectName'] == 'Updated ' + name
    second = call('POST', path, owner, body, 201)['data']['projectId']
    call('PUT', path + f'/{second}', owner, {**body, 'projectName': 'Updated ' + name}, 409, 'rename conflict')
    assert call('GET', path + f'/{second}', owner)['data']['projectName'] == name
    assert sql(f"SELECT COUNT(*) FROM dbo.ActivityLog WHERE ProjectId={second} AND Action='PROJECT_UPDATED'") == '0'
    passed.append('conflicting rename rolls back project and log')
    call('PATCH', path + f'/{pid}/close', owner, label='manager close')
    call('PATCH', path + f'/{pid}/close', owner, expected=409, label='repeat close conflict')
    call('PUT', path + f'/{pid}', owner, body, 409, 'closed project cannot update')
    assert call('GET', path + '?status=CLOSED', owner)['data']['total'] == 1
    call('DELETE', path + f'/{pid}', owner, label='manager soft delete')
    call('GET', path + f'/{pid}', owner, expected=404, label='deleted detail hidden')
    call('DELETE', path + f'/{pid}', owner, expected=404, label='repeat delete missing')
    assert call('GET', path, member)['data']['total'] == 0
    assert sql(f"SELECT Status FROM dbo.Project WHERE ProjectId={pid}") == 'DELETED'
    assert sql(f'SELECT COUNT(*) FROM dbo.BoardColumn WHERE ProjectId={pid}') == '4'
    passed.append('soft delete preserves related data and hides project from lists')
    # Concurrent requests must resolve to exactly one creation, one conflict.
    concurrent_body = {'projectName': 'Concurrent ' + uuid.uuid4().hex}
    def create_concurrent(_):
        data = json.dumps(concurrent_body).encode()
        request = urllib.request.Request(BASE + path, data=data, headers={
            'Content-Type': 'application/json', 'Authorization': 'Bearer ' + owner}, method='POST')
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                response.read()
                return response.code
        except urllib.error.HTTPError as error:
            error.read()
            return error.code
    with concurrent.futures.ThreadPoolExecutor(2) as pool:
        assert sorted(pool.map(create_concurrent, range(2))) == [201, 409]
    passed.append('concurrent duplicate creation: exactly one 201 and one 409')
    # A revoked token cannot create projects.
    call('POST', '/api/v1/auth/logout', owner)
    call('POST', path, owner, body, 401, 'revoked token denied')
    print(f'PASS ALL: {len(passed)} checks', flush=True)


if __name__ == '__main__':
    main()
