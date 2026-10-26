"""Minimal standard-library SignalR JSON/WebSocket receiver for integration tests."""
import base64,json,os,socket,struct,time,urllib.parse,urllib.request
from projects_integration import BASE

class SignalR:
    def __init__(self,token,hub='/hubs/notifications'):
        request=urllib.request.Request(BASE+hub+'/negotiate?negotiateVersion=1',data=b'',headers={'Authorization':'Bearer '+token},method='POST')
        with urllib.request.urlopen(request,timeout=15) as response:connection=json.loads(response.read())['connectionToken']
        url=urllib.parse.urlsplit(BASE);self.sock=socket.create_connection((url.hostname,url.port),timeout=15);self.buffer=b'';self.records=[]
        path=hub+'?'+urllib.parse.urlencode({'id':connection,'access_token':token})
        key=base64.b64encode(os.urandom(16)).decode()
        self.sock.sendall((f'GET {path} HTTP/1.1\r\nHost: {url.netloc}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n').encode())
        while b'\r\n\r\n' not in self.buffer:self.buffer+=self.sock.recv(4096)
        header,self.buffer=self.buffer.split(b'\r\n\r\n',1);assert b'101' in header.split(b'\r\n',1)[0],header
        self.send({'protocol':'json','version':1});assert self.receive(10)=={}
    def exact(self,size):
        while len(self.buffer)<size:
            data=self.sock.recv(4096)
            if not data:raise EOFError('WebSocket closed')
            self.buffer+=data
        value,self.buffer=self.buffer[:size],self.buffer[size:];return value
    def send(self,message):
        data=(json.dumps(message)+'\x1e').encode();mask=os.urandom(4);size=len(data)
        header=b'\x81'+(bytes([128+size]) if size<126 else b'\xfe'+struct.pack('>H',size))
        self.sock.sendall(header+mask+bytes(value^mask[index%4] for index,value in enumerate(data)))
    def receive(self,timeout=5):
        if self.records:return self.records.pop(0)
        self.sock.settimeout(timeout)
        try:
            head=self.exact(2);opcode=head[0]&15;length=head[1]&127
            if length==126:length=struct.unpack('>H',self.exact(2))[0]
            if length==127:length=struct.unpack('>Q',self.exact(8))[0]
            mask=self.exact(4) if head[1]&128 else None
            data=self.exact(length)
            if mask:data=bytes(value^mask[index%4] for index,value in enumerate(data))
            if opcode==8:return {'closed':True}
            # Small test events fit one frame; store coalesced records separately.
            records=[json.loads(item) for item in data.decode().split('\x1e') if item]
            self.records.extend(records)
            return self.records.pop(0) if self.records else {}
        except socket.timeout:return None
    def event(self,target='notificationReceived',timeout=10):
        end=time.monotonic()+timeout
        while time.monotonic()<end:
            item=self.receive(max(.1,end-time.monotonic()))
            if item is None:return None
            if item.get('type')==1 and item.get('target')==target:return item['arguments'][0]
        return None
    def close(self):self.sock.close()
