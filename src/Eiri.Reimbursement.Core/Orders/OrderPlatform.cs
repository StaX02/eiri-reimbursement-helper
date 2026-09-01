namespace Eiri.Reimbursement.Core.Orders;

public enum OrderPlatform
{
    Other = 0,
    Taobao = 1,
    JD = 2,
}

public static class OrderPlatformExtensions
{
    public static string ToDisplayName(this OrderPlatform platform) => platform switch
    {
        OrderPlatform.Taobao => "淘宝",
        OrderPlatform.JD => "京东",
        OrderPlatform.Other => "其他平台",
        _ => platform.ToString(),
    };
}
