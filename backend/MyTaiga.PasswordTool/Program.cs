using System.Text;

Console.OutputEncoding = Encoding.UTF8;

Console.Write("Nhập mật khẩu mới: ");
var password = ReadHiddenPassword();

Console.Write("Nhập lại mật khẩu: ");
var confirmation = ReadHiddenPassword();

if (password != confirmation)
{
    Console.WriteLine("Hai mật khẩu không khớp.");
    return;
}

if (password.Length < 8)
{
    Console.WriteLine("Mật khẩu phải có ít nhất 8 ký tự.");
    return;
}

if (Encoding.UTF8.GetByteCount(password) > 72)
{
    Console.WriteLine("Mật khẩu không được vượt quá 72 byte UTF-8.");
    return;
}

var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

Console.WriteLine();
Console.WriteLine("Hash BCrypt:");
Console.WriteLine(hash);

static string ReadHiddenPassword()
{
    var value = new StringBuilder();

    while (true)
    {
        var key = Console.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return value.ToString();
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (value.Length > 0)
                value.Length--;

            continue;
        }

        if (!char.IsControl(key.KeyChar))
            value.Append(key.KeyChar);
    }
}