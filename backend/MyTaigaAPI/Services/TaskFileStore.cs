using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace MyTaigaAPI.Services;

public class TaskFileStore(IWebHostEnvironment environment,IConfiguration configuration)
{
    public string Root=>Path.GetFullPath(configuration["Storage:TaskFilesRoot"]??Path.Combine(environment.ContentRootPath,"App_Data","task-files"));
    public static readonly IReadOnlyDictionary<string,string> MimeTypes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"]="application/pdf",[".txt"]="text/plain; charset=utf-8",[".png"]="image/png",[".jpg"]="image/jpeg",[".jpeg"]="image/jpeg",
        [".zip"]="application/zip",[".docx"]="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"]="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",[".pptx"]="application/vnd.openxmlformats-officedocument.presentationml.presentation"
    };
    public string Resolve(string key)
    {
        if(!Regex.IsMatch(key,@"\A[a-f0-9]{32}\.(pdf|txt|png|jpg|jpeg|zip|docx|xlsx|pptx)\z"))throw new ArgumentException("Mã lưu tệp không hợp lệ.");
        return Path.Combine(Root,key);
    }
    public static string FileName(string name)
    {
        var leaf=Path.GetFileName(name.Replace('\\','/'));
        if(string.IsNullOrWhiteSpace(leaf)||leaf.Length>200||leaf.Any(char.IsControl)||leaf.IndexOfAny(Path.GetInvalidFileNameChars())>=0)
            throw new ArgumentException("Tên tệp không hợp lệ hoặc dài quá 200 ký tự.");
        return leaf;
    }
    public static void Validate(string path,string extension)
    {
        using var file=File.OpenRead(path);var header=new byte[8];var count=file.Read(header);file.Position=0;
        if(extension==".txt")
        {
            using var reader=new StreamReader(file,new UTF8Encoding(false,true),false);
            var buffer=new char[8192];int length;
            while((length=reader.Read(buffer,0,buffer.Length))>0)if(buffer.AsSpan(0,length).Contains('\0'))throw new ArgumentException("Tệp text chứa dữ liệu nhị phân.");
            return;
        }
        if(extension is ".png" or ".jpg" or ".jpeg")
        {
            using var codec=SKCodec.Create(file);
            if(codec is null||codec.Info.Width<=0||codec.Info.Height<=0||codec.Info.Width>8192||codec.Info.Height>8192||
                (long)codec.Info.Width*codec.Info.Height>16000000)throw new ArgumentException("Ảnh không hợp lệ hoặc vượt 16 triệu pixel.");
            if((extension==".png"&&codec.EncodedFormat!=SKEncodedImageFormat.Png)||(extension!=".png"&&codec.EncodedFormat!=SKEncodedImageFormat.Jpeg))
                throw new ArgumentException("Nội dung ảnh không khớp đuôi tệp.");
            using var bitmap=new SKBitmap(codec.Info);
            if(codec.GetPixels(bitmap.Info,bitmap.GetPixels())!=SKCodecResult.Success)throw new ArgumentException("Ảnh bị hỏng.");
            return;
        }
        if(extension==".pdf")
        {
            if(count<5||Encoding.ASCII.GetString(header,0,5)!="%PDF-")throw new ArgumentException("Nội dung không phải PDF.");
            file.Position=Math.Max(0,file.Length-1024);using var reader=new StreamReader(file,Encoding.ASCII);
            if(!reader.ReadToEnd().Contains("%%EOF"))throw new ArgumentException("PDF thiếu dấu kết thúc.");return;
        }
        using var archive=new ZipArchive(file,ZipArchiveMode.Read);
        if(archive.Entries.Count>10000)throw new ArgumentException("Gói ZIP có quá nhiều mục.");
        long total=0;
        foreach(var entry in archive.Entries)
        {if(entry.Length<0||entry.Length>268435456-total)throw new ArgumentException("Gói ZIP vượt giới hạn nội dung.");total+=entry.Length;}
        if(extension==".zip")return;
        var main=extension switch{".docx"=>"word/document.xml",".xlsx"=>"xl/workbook.xml",".pptx"=>"ppt/presentation.xml",_=>""};
        if(archive.GetEntry("[Content_Types].xml") is null||archive.GetEntry(main) is null||archive.Entries.Any(e=>e.Name.Equals("vbaProject.bin",StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Tệp Office không đúng cấu trúc hoặc có macro.");
    }
}
