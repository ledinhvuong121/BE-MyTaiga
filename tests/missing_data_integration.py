import json,uuid,urllib.request,urllib.error
from pathlib import Path
from projects_integration import call,sql,user,token_for_admin,passed,BASE
from attachments_integration import upload,read,png
from signalr_client import SignalR

def main():
 oid,o=user();mid,m=user();_,outsider=user();admin=token_for_admin()
 project=call('POST','/api/v1/projects',o,{'projectName':'Enriched '+uuid.uuid4().hex},201)['data']['projectId']
 call('POST',f'/api/v1/projects/{project}/members',o,{'userId':mid},201)
 epic=call('POST',f'/api/v1/projects/{project}/epics',o,{'title':'Real Epic','color':'#6366f1'},201)['data']['epicId']
 task=call('POST',f'/api/v1/projects/{project}/tasks',o,{'title':'Real task','assigneeId':mid,'epicId':epic,'tags':['backend','api'],'storyPoints':8},201)['data']
 tid=task['taskId'];assert task['assignee']['userId']==mid and task['assignee']['fullName']=='Project integration user'
 assert task['assignee']['role']=='MEMBER' and task['reporter']['userId']==oid
 assert task['epic']['color']=='#6366f1' and task['tags']==['backend','api'] and task['storyPoints']==8
 passed.append('Task returns actual assignee reporter Epic tags and story points')
 board=call('GET',f'/api/v1/projects/{project}/board',m)['data'];card=next(t for c in board['columns'] for t in c['tasks'] if t['taskId']==tid)
 assert card['assigneeName']==task['assigneeName'] and card['epicColor']=='#6366f1' and card['tags']==task['tags'];passed.append('Board cards enriched consistently')
 call('PUT',f'/api/v1/tasks/{tid}',m,{'title':'Updated task','tags':['qa'],'storyPoints':5},label='Task tags and story points editable')
 detail=call('GET',f'/api/v1/tasks/{tid}',m)['data']['task'];assert detail['tags']==['qa'] and detail['storyPoints']==5 and '42' not in detail['updatedAtFormatted']
 call('POST',f'/api/v1/projects/{project}/tasks',o,{'title':'bad','tags':['x'*51]},400,label='tag length validation')
 call('POST',f'/api/v1/projects/{project}/tasks',o,{'title':'bad','tags':['x']*21},400)
 call('POST',f'/api/v1/projects/{project}/tasks',o,{'title':'bad','storyPoints':-1},400,label='negative story points rejected')
 call('POST',f'/api/v1/projects/{project}/epics',o,{'title':'bad','color':'javascript:bad'},400,label='Epic color validation')
 comment=call('POST',f'/api/v1/tasks/{tid}/comments',m,{'content':'actual author'},201)['data'];assert comment['author']['userId']==mid and comment['isEditable']
 other=call('GET',f'/api/v1/tasks/{tid}/comments',o)['data']['items'][0];assert other['authorName'] and not other['isEditable'];passed.append('Comment author and editability depend on viewer')
 notices=call('GET','/api/v1/notifications',m)['data']['items'];notice=next(n for n in notices if n['type']=='TASK_ASSIGNED' and n['refId']==tid)
 assert notice['userId']==mid and notice['actorId']==oid and notice['actor']['actorId']==oid and notice['actorName'] and notice['actorRole']=='MANAGER' and notice['targetTitle']=='Updated task'
 passed.append('Notification actor is sender not recipient')
 sql(f"INSERT dbo.Notification(UserId,Type,Title,CreatedAt,RefType) VALUES({mid},'SYSTEM','Legacy unknown actor',SYSUTCDATETIME(),'SYSTEM')")
 old=next(n for n in call('GET','/api/v1/notifications',m)['data']['items'] if n['title']=='Legacy unknown actor');assert old['actor'] is None and old['actorId'] is None;passed.append('Legacy notification actor remains null instead of fabricated')
 logs=call('GET',f'/api/v1/projects/{project}/activity-logs?action=TASK_UPDATE',m)['data']['items'];assert logs and logs[0]['userName'] and logs[0]['entityTitle']=='Updated task' and logs[0]['changes']['title']=={'from':'Real task','to':'Updated task'}
 passed.append('Activity user title and actual old/new changes')
 bug=call('POST','/api/v1/bugs',m,{'taskId':tid,'title':'Actual bug','description':'Steps to reproduce','severity':'MINOR'},201)['data'];assert bug['reporterName'] and bug['taskTitle']=='Updated task' and bug['taskCode']==f'#TASK-{tid}'
 passed.append('Bug reporter and linked Task metadata')
 file=upload(f'/api/v1/tasks/{tid}/attachments',m,[('real-name.png',png())])['data']['items'][0]
 assert file['originalFileName']=='real-name.png' and file['mimeType']=='image/png' and file['fileSizeBytes']==len(png()) and file['uploadedByName']
 stored=sql(f"SELECT FilePath FROM dbo.Attachment WHERE AttachmentId={file['attachmentId']}");assert stored.startswith('https://res.cloudinary.com/') and '/authenticated/' in stored and '/s--' not in stored
 assert file['previewUrl'].startswith('http://127.0.0.1:5162/') and read(file['previewUrl'],m)[0]==png()
 passed.append('Task image stored as Cloudinary URL with signed delivery and original metadata')
 call('GET',f"/api/v1/attachments/{file['attachmentId']}",outsider,expected=403,label='Cloud image metadata still project scoped')
 root=Path(__file__).resolve().parents[1]/'backend/MyTaigaAPI/App_Data/test-task-files';before=set(root.glob('*'));count=sql(f'SELECT COUNT(*) FROM dbo.Attachment WHERE TaskId={tid}')
 upload(f'/api/v1/tasks/{tid}/attachments',m,[('cloud-fail.png',png()+b'cloud-fail')],503,'Cloud upload failure controlled 503')
 assert sql(f'SELECT COUNT(*) FROM dbo.Attachment WHERE TaskId={tid}')==count and set(root.glob('*'))==before;passed.append('Cloud failure rolls back database and temporary file')
 upload(f'/api/v1/tasks/{tid}/attachments',m,[('uploaded.png',png()),('bad.exe',b'MZ')],400,'Cloud batch rollback')
 assert sql(f'SELECT COUNT(*) FROM dbo.Attachment WHERE TaskId={tid}')==count
 passed.append('Successful cloud asset compensated when later batch item fails')
 rid=call('POST','/api/v1/chat/rooms',o,{'roomType':'PRIVATE','recipientId':mid},201)['data']['chatRoomId']
 live=SignalR(m,'/hubs/chat')
 rooms=call('GET','/api/v1/chat/rooms',o)['data'];room=next(r for r in rooms if r['chatRoomId']==rid)
 assert room['otherParticipantName'] and room['otherParticipantStatus']=='ONLINE' and room['participantsCount']==2;passed.append('Private room identifies other participant and real online presence')
 msg=call('POST',f'/api/v1/chat/rooms/{rid}/messages',o,{'content':'Original'},201)['data'];assert msg['senderName'] and msg['senderAvatarCode'] and not msg['isEdited']
 event=live.event('message.new',15);assert event['senderName'] and event['messageId']==msg['messageId'];passed.append('Realtime chat contains enriched sender')
 reply=call('POST',f'/api/v1/chat/rooms/{rid}/messages',m,{'content':'Reply','replyToMessageId':msg['messageId']},201)['data'];assert reply['replyToMessageId']==msg['messageId'];passed.append('Reply reference persisted')
 call('PUT',f"/api/v1/chat/rooms/{rid}/messages/{msg['messageId']}",m,{'content':'not mine'},403,label='only sender can edit message')
 edited=call('PUT',f"/api/v1/chat/rooms/{rid}/messages/{msg['messageId']}",o,{'content':'Edited'})['data'];assert edited['isEdited'] and edited['editedAt'];passed.append('Message edit and timestamp persisted')
 chatfile=upload(f'/api/v1/chat/rooms/{rid}/messages',m,[('chat.png',png())])['data']['attachments'][0]
 assert chatfile['url'] and read(chatfile['previewUrl'],o)[0]==png();passed.append('Chat image also Cloudinary URL and signed preview')
 room=next(r for r in call('GET','/api/v1/chat/rooms',o)['data'] if r['chatRoomId']==rid);assert room['lastMessageSenderName'] and room['lastMessage']
 passed.append('Room has latest message and sender name')
 live.close()
 call('POST','/api/v1/admin/media/migrate-images',m,{'execute':False},403,label='migration restricted to Admin')
 call('POST','/api/v1/admin/media/migrate-images',admin,{'execute':False},label='migration dry run available')
 # Metadata fields omitted by an older client must not silently erase tags/story points.
 call('PUT',f'/api/v1/tasks/{tid}',m,{'title':'Compatible title'})
 retained=call('GET',f'/api/v1/tasks/{tid}',m)['data']['task'];assert retained['tags']==['qa'] and retained['storyPoints']==5
 passed.append('legacy update preserves omitted tags/story points')
 tags=['Tiếng Việt '+str(i)+' ăấềộử'*4 for i in range(20)]
 call('PUT',f'/api/v1/tasks/{tid}',m,{'title':'Vietnamese tags','tags':tags})
 assert call('GET',f'/api/v1/tasks/{tid}',m)['data']['task']['tags']==tags
 passed.append('twenty Vietnamese tags persist without JSON truncation')
 # Explicit cursor permits skipping missing old source files when migrating.
 avatar_root=Path(__file__).resolve().parents[1]/'backend/MyTaigaAPI/App_Data/test-avatars';avatar_root.mkdir(exist_ok=True,parents=True)
 filename=uuid.uuid4().hex+'.png';(avatar_root/filename).write_bytes(png())
 legacy='/api/v1/users/me/avatar/'+filename
 sql(f"UPDATE dbo.[User] SET Avatar='{legacy}' WHERE UserId={mid}")
 migrated=call('POST','/api/v1/admin/media/migrate-images',admin,{'execute':True,'batchSize':1,'afterUserId':mid-1,'afterAttachmentId':999999999,'afterMessageAttachmentId':999999999})['data']
 assert migrated['items'][0]['status']=='MIGRATED' and sql(f'SELECT Avatar FROM dbo.[User] WHERE UserId={mid}').startswith('https://res.cloudinary.com/')
 assert (avatar_root/filename).exists();passed.append('legacy avatar migration persists URL and retains source backup')
 assert call('GET',f'/api/v1/tasks/{tid}/comments',o)['data']['items'][0]['authorAvatarUrl'].startswith('https://res.cloudinary.com/')
 bad=int(sql(f"INSERT dbo.Attachment(TaskId,FileName,FilePath,FileSize,UploadedBy,UploadedAt) VALUES({tid},'legacy.png','../../escape.png',10,{mid},SYSUTCDATETIME());SELECT SCOPE_IDENTITY()"))
 invalid=call('POST','/api/v1/admin/media/migrate-images',admin,{'execute':True,'batchSize':1,'afterUserId':999999999,'afterAttachmentId':bad-1,'afterMessageAttachmentId':999999999})['data']
 assert invalid['items'][0]['status']=='SOURCE_INVALID'
 print(f'PASS ALL: {len(passed)} checks')
if __name__=='__main__':main()
