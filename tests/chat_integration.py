import uuid,concurrent.futures
from projects_integration import call,sql,user,token_for_admin,passed
from signalr_client import SignalR
from attachments_integration import upload,read

def main():
    oid,o=user();mid,m=user();xid,x=user();admin=token_for_admin()
    p=call('POST','/api/v1/projects',o,{'projectName':'Chat '+uuid.uuid4().hex},201)['data']['projectId']
    call('POST',f'/api/v1/projects/{p}/members',o,{'userId':mid},201)
    call('GET','/api/v1/chat/rooms',expected=401,label='anonymous chat denied')
    call('POST','/api/v1/chat/rooms',o,{'roomType':'PRIVATE','recipientId':oid},400,label='self room rejected')
    call('POST','/api/v1/chat/rooms',o,{'roomType':'PRIVATE','recipientId':xid},403,label='private recipient must share project')
    body={'roomType':'PRIVATE','recipientId':mid}
    rid=call('POST','/api/v1/chat/rooms',o,body,201,label='create private room')['data']['chatRoomId']
    assert call('POST','/api/v1/chat/rooms',m,{'roomType':'PRIVATE','recipientId':oid})['data']['chatRoomId']==rid
    passed.append('reverse private pair reuses room')
    group={'roomType':'GROUP','projectId':p,'roomName':'Team','participantIds':[mid]}
    call('POST','/api/v1/chat/rooms',m,group,403,label='member cannot create group')
    gid=call('POST','/api/v1/chat/rooms',o,group,201,label='manager creates group')['data']['chatRoomId']
    call('POST',f'/api/v1/chat/rooms/{gid}/participants',o,{'userIds':[xid]},400,label='group rejects cross-project participant')
    call('POST',f'/api/v1/chat/rooms/{gid}/participants',m,{'userIds':[mid]},403,label='member cannot manage participants')
    call('GET',f'/api/v1/chat/rooms/{rid}/messages',x,expected=404,label='outsider history hidden')
    call('GET',f'/api/v1/chat/rooms/{rid}/messages',admin,expected=404,label='Admin no private chat bypass')
    live=SignalR(m,'/hubs/chat');notices=SignalR(m,'/hubs/chat');isolated=SignalR(x,'/hubs/chat')
    msg=call('POST',f'/api/v1/chat/rooms/{rid}/messages',o,{'content':'Hello Vietnamese'},201,label='send text')['data']
    event=live.event('message.new',15);assert event and event['messageId']==msg['messageId']
    assert isolated.event('message.new',1) is None;passed.append('real SignalR message only to participant')
    notice=notices.event('notification.new',10);assert notice and notice['refId']==msg['messageId'];passed.append('report notification.new event on chat hub')
    assert call('GET','/api/v1/chat/unread-count',m)['data']['unreadCount']==1
    call('POST',f'/api/v1/chat/rooms/{rid}/messages',o,{'content':' '},400,label='empty message rejected')
    call('POST',f'/api/v1/chat/rooms/{rid}/messages',o,{'content':'x'*5001},400,label='oversized message rejected')
    msg2=call('POST',f'/api/v1/chat/rooms/{rid}/messages',o,{'content':'Second'},201)['data']
    hist=call('GET',f'/api/v1/chat/rooms/{rid}/messages?limit=1',m)['data'];assert hist['hasMore'] and hist['items'][0]['messageId']==msg2['messageId']
    hist2=call('GET',f'/api/v1/chat/rooms/{rid}/messages?before='+str(hist['nextBefore']),m)['data'];assert hist2['items'][0]['messageId']==msg['messageId'];passed.append('cursor pagination no duplicate')
    call('GET',f'/api/v1/chat/rooms/{rid}/messages?limit=101',m,expected=400,label='history limit validation')
    call('POST',f'/api/v1/chat/rooms/{rid}/read',m,{'messageId':msg2['messageId']},label='mark messages read')
    call('POST',f'/api/v1/chat/rooms/{rid}/read',m,{'messageId':msg['messageId']})
    assert call('GET','/api/v1/chat/unread-count',m)['data']['unreadCount']==0;passed.append('read cursor never regresses')
    file=upload(f'/api/v1/chat/rooms/{gid}/messages',o,[('note.txt',b'private chat file')],201,'file-only chat message')['data']['attachments'][0]
    path=file['downloadUrl'];assert read(path,m)[0]==b'private chat file'
    read(path,x,404);read(path,None,401);passed.append('chat files authenticated and room scoped')
    upload(f'/api/v1/chat/rooms/{gid}/messages',o,[('bad.exe',b'bad')],400,'chat rejects executable')
    assert read(path+'/preview',m)[0]==b'private chat file'
    passed.append('authenticated text preview')
    call('GET',f'/api/v1/chat/rooms/{gid}/participants',m,label='list room participants')
    call('DELETE',f'/api/v1/chat/rooms/{gid}/participants/{mid}',m,expected=403,label='member cannot remove participant')
    call('DELETE',f'/api/v1/chat/rooms/{gid}/participants/{mid}',o,label='manager removes participant preserving messages')
    call('GET',f'/api/v1/chat/rooms/{gid}/messages',m,expected=404,label='removed participant loses history')
    read(path,m,404)
    call('POST',f'/api/v1/chat/rooms/{gid}/participants',o,{'userIds':[mid]},label='rejoin preserves message foreign keys')
    sql(f"UPDATE dbo.Project SET Status='CLOSED' WHERE ProjectId={p}")
    call('POST',f'/api/v1/chat/rooms/{gid}/messages',o,{'content':'closed'},404,label='closed project group is read-only')
    call('GET',f'/api/v1/chat/rooms/{gid}/messages',m)
    sql(f'UPDATE dbo.Project SET Status=\'ACTIVE\' WHERE ProjectId={p}')
    call('DELETE',f'/api/v1/projects/{p}/members/{mid}',o)
    call('GET',f'/api/v1/chat/rooms/{rid}/messages',m,expected=404,label='departed member loses private room shared scope')
    call('GET',f'/api/v1/chat/rooms/{gid}/messages',m,expected=404,label='departed member loses group history')
    read(path,m,404);passed.append('departed member loses chat files')
    live.close();notices.close();isolated.close()
    print(f'PASS ALL: {len(passed)} checks')
if __name__=='__main__':main()
