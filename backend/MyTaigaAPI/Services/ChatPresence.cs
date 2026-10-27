using System.Collections.Concurrent;
namespace MyTaigaAPI.Services;
public class ChatPresence
{
    readonly ConcurrentDictionary<string,(long User,int Version,DateTime Expires)> connections=new();
    public void Connect(string connection,long user,int version,DateTime expires)=>connections[connection]=(user,version,expires);
    public void Disconnect(string connection)=>connections.TryRemove(connection,out _);
    public bool Online(long user,int version)=>connections.Values.Any(c=>c.User==user&&c.Version==version&&c.Expires>DateTime.UtcNow);
}
