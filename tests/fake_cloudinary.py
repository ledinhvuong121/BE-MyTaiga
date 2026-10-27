import hashlib,io,json,threading,time,urllib.parse
from email.parser import BytesParser
from email.policy import default
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
ASSETS={};EVENTS=[];LOCK=threading.Lock()
class Handler(BaseHTTPRequestHandler):
 def log_message(self,*args):pass
 def reply(self,code,data,kind='application/json'):
  self.send_response(code);self.send_header('Content-Type',kind);self.end_headers();self.wfile.write(json.dumps(data).encode() if kind=='application/json' else data)
 def do_POST(self):
  raw=self.rfile.read(int(self.headers.get('Content-Length',0)))
  if 'multipart/form-data' in self.headers.get('Content-Type',''):
   message=BytesParser(policy=default).parsebytes(('Content-Type: '+self.headers['Content-Type']+'\r\nMIME-Version: 1.0\r\n\r\n').encode()+raw)
   params={};content=b''
   for part in message.iter_parts():
    name=part.get_param('name',header='content-disposition');data=part.get_payload(decode=True)
    if name=='file':content=data;filename=part.get_filename()
    else:params[name]=data.decode()
  else:params={k:v[0] for k,v in urllib.parse.parse_qs(raw.decode()).items()};content=b''
  signature=params.pop('signature','');key=params.pop('api_key','')
  expected=hashlib.sha256(('&'.join(k+'='+v for k,v in sorted(params.items()))+'test-secret').encode()).hexdigest()
  if signature!=expected or key!='test-key':return self.reply(401,{'error':'signature'})
  if self.path.endswith('/upload'):
   if b'cloud-fail' in content:return self.reply(503,{'error':'simulated'})
   ext='png' if content.startswith(b'\x89PNG') else 'jpg'
   public=params['public_id'];kind=params['type']
   with LOCK:ASSETS[(kind,public,ext)]=content;EVENTS.append({'action':'upload','type':kind,'public_id':public})
   signature_segment='s--test-signature--/' if kind=='authenticated' else ''
   return self.reply(200,{'secure_url':f'https://res.cloudinary.com/mytaiga-test/image/{kind}/{signature_segment}v1/{public}.{ext}','public_id':public,'resource_type':'image'})
  if self.path.endswith('/destroy'):
   with LOCK:
    for k in list(ASSETS):
     if k[0]==params['type'] and k[1]==params['public_id']:del ASSETS[k]
    EVENTS.append({'action':'destroy','public_id':params['public_id']})
   return self.reply(200,{'result':'ok'})
  self.reply(404,{})
 def do_GET(self):
  if self.path=='/test/events':return self.reply(200,EVENTS)
  params={k:v[0] for k,v in urllib.parse.parse_qs(urllib.parse.urlsplit(self.path).query).items()}
  sig=params.pop('signature','');key=params.pop('api_key','');expected=hashlib.sha256(('&'.join(k+'='+v for k,v in sorted(params.items()))+'test-secret').encode()).hexdigest()
  if key!='test-key' or sig!=expected or int(params.get('expires_at','0'))<time.time():return self.reply(403,{})
  content=ASSETS.get((params.get('type'),params.get('public_id'),params.get('format')))
  if content is None:return self.reply(404,{})
  self.reply(200,content,'image/png' if params['format']=='png' else 'image/jpeg')
if __name__=='__main__':
 print('Cloudinary test double on loopback 5162; no real upload',flush=True);ThreadingHTTPServer(('127.0.0.1',5162),Handler).serve_forever()
