using System.IO;
using System.Windows.Media.Imaging;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Desktop;

public sealed class DingTalkImageMetadataReader : IDingTalkImageMetadataReader
{
    public DingTalkImageMetadata Read(Stream stream, string fileName)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        string extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        string mime = decoder switch
        {
            PngBitmapDecoder when extension == "png" => "image/png",
            JpegBitmapDecoder when extension is "jpg" or "jpeg" => "image/jpeg",
            GifBitmapDecoder when extension == "gif" => "image/gif",
            BmpBitmapDecoder when extension == "bmp" => "image/bmp",
            _ => throw new InvalidOperationException("图片格式与扩展名不匹配。")
        };
        var frame = decoder.Frames[0];
        return new(frame.PixelWidth, frame.PixelHeight, extension, mime);
    }
}
