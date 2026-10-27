using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace MyTaigaAPI.Services;
public class CloudMediaException(string message):Exception(message);
public class CloudinaryImages(HttpClient http,IConfiguration config,IWebHostEnvironment env,ILogger<CloudinaryImages> logger)
{
    string Required(string key)=>!string.IsNullOrWhiteSpace(config["Cloudinary:"+key])?config["Cloudinary:"+key]!:throw new CloudMediaException("Chưa cấu hình Cloudinary:"+key+" trong User Secrets.");
    string Cloud=>Required("CloudName");
    string ApiBase
    {
        get
        {
            var value=config["Cloudinary:ApiBase"]??"https://api.cloudinary.com/";
            if(value!="https://api.cloudinary.com/"&&!(env.IsDevelopment()&&Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.IsLoopback))throw new CloudMediaException("Cloudinary endpoint không hợp lệ.");
            return value.TrimEnd('/')+"/v1_1/"+Uri.EscapeDataString(Cloud)+"/image/";
        }
    }
    public bool IsCloud(string? value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="res.cloudinary.com"&&uri.AbsolutePath.StartsWith('/'+Cloud+"/image/",StringComparison.Ordinal);
    (string Type,string PublicId,string Format) Parts(string url)
    {
        if(!IsCloud(url))throw new CloudMediaException("URL ảnh không thuộc Cloudinary của dự án.");
        var path=new Uri(url).AbsolutePath.Split('/',StringSplitOptions.RemoveEmptyEntries);
        if(path.Length<5||path[2] is not("authenticated" or "upload"))throw new CloudMediaException("URL Cloudinary không hợp lệ.");
        var start=Regex.IsMatch(path[3],"^v[0-9]+$")?4:3;var full=string.Join('/',path.Skip(start));var ext=Path.GetExtension(full);
        if(ext.Length<2)throw new CloudMediaException("Ảnh thiếu định dạng.");return(path[2],Uri.UnescapeDataString(full[..^ext.Length]),ext[1..]);
    }
    public string Delivery(string url,bool attachment=false)
    {
        var p=Parts(url);if(p.Type=="upload")return url;
        var parameters=new SortedDictionary<string,string>(StringComparer.Ordinal){["attachment"]=attachment?"true":"false",["expires_at"]=DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString(),["format"]=p.Format,["public_id"]=p.PublicId,["timestamp"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),["type"]=p.Type};
        var signature=Sign(parameters);parameters["api_key"]=Required("ApiKey");parameters["signature"]=signature;
        return ApiBase+"download?"+string.Join('&',parameters.Select(kv=>Uri.EscapeDataString(kv.Key)+"="+Uri.EscapeDataString(kv.Value)));
    }
    string Sign(SortedDictionary<string,string> values)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('&',values.Select(v=>v.Key+"="+v.Value))+Required("ApiSecret")))).ToLowerInvariant();
    public async Task<string> UploadAsync(Stream input,string name,bool privateImage,CancellationToken ct)
    {
        var api=ApiBase;var publicId="mytaiga/"+(privateImage?"private/":"avatars/")+Guid.NewGuid().ToString("N");
        var parameters=new SortedDictionary<string,string>(StringComparer.Ordinal){["public_id"]=publicId,["timestamp"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),["type"]=privateImage?"authenticated":"upload"};
        using var form=new MultipartFormDataContent();foreach(var pair in parameters)form.Add(new StringContent(pair.Value),pair.Key);
        form.Add(new StringContent(Required("ApiKey")),"api_key");form.Add(new StringContent(Sign(parameters)),"signature");
        form.Add(new StreamContent(input),"file",name);
        try
        {
            using var result=await http.PostAsync(api+"upload",form,ct);
            if(!result.IsSuccessStatusCode)throw new CloudMediaException("Cloudinary từ chối upload; kiểm tra cấu hình/quota.");
            using var doc=JsonDocument.Parse(await result.Content.ReadAsStringAsync(ct));var url=doc.RootElement.GetProperty("secure_url").GetString();
            if(url is null||!IsCloud(url))throw new CloudMediaException("Cloudinary trả URL không hợp lệ.");var parsed=Parts(url);
            if(parsed.PublicId!=publicId||parsed.Type!=parameters["type"])throw new CloudMediaException("Cloudinary trả tài nguyên không khớp.");return url;
        }
        catch(CloudMediaException){await DeleteAsync($"https://res.cloudinary.com/{Cloud}/image/{parameters["type"]}/v1/{publicId}.png");throw;}
        catch(HttpRequestException){await DeleteAsync($"https://res.cloudinary.com/{Cloud}/image/{parameters["type"]}/v1/{publicId}.png");throw new CloudMediaException("Không kết nối được Cloudinary.");}
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new CloudMediaException("Upload Cloudinary quá thời gian.");}
        catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException){throw new CloudMediaException("Phản hồi Cloudinary không hợp lệ.");}
    }
    public async Task DeleteAsync(string url)
    {
        try
        {
            var p=Parts(url);var values=new SortedDictionary<string,string>(StringComparer.Ordinal){["public_id"]=p.PublicId,["timestamp"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),["type"]=p.Type,["invalidate"]="true"};
            var data=values.ToDictionary();data["api_key"]=Required("ApiKey");data["signature"]=Sign(values);
            using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(15));using var result=await http.PostAsync(ApiBase+"destroy",new FormUrlEncodedContent(data),cts.Token);
            if(!result.IsSuccessStatusCode)logger.LogWarning("Cloudinary cleanup needs retry.");
        }
        catch(Exception e)when(e is CloudMediaException or HttpRequestException or OperationCanceledException){logger.LogWarning("Cloudinary cleanup deferred; asset may be orphaned.");}
    }
}
