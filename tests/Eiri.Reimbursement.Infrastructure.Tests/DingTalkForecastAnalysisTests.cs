using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Infrastructure.Tests;

public sealed class DingTalkForecastAnalysisTests
{
    [Theory]
    [InlineData("[]", false)]
    [InlineData("[{\"activityType\":\"target_approval\"}]", false)]
    [InlineData("[{\"activityType\":\"target_select\"}]", true)]
    [InlineData("[{\"isTargetSelect\":true}]", true)]
    [InlineData("[{\"isTargetSelect\":false}]", false)]
    [InlineData("[{\"activityType\":\"target_approval\"},{\"activityType\":\"target_select\"}]", true)]
    public void DetectsSelectionNodes(string rules, bool expected)
    {
        using var document = JsonDocument.Parse("{\"result\":{\"isForecastSuccess\":true,\"workflowActivityRules\":" + rules + "}}");
        Assert.Equal(expected, DingTalkForecastAnalysis.Analyze(document.RootElement).HasSelfSelectedNodes);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"result\":{\"isForecastSuccess\":false,\"workflowActivityRules\":[]}}")]
    [InlineData("{\"result\":{\"isForecastSuccess\":true}}")]
    [InlineData("{\"result\":{\"isForecastSuccess\":true,\"workflowActivityRules\":[{}]}}")]
    [InlineData("{\"result\":{\"isForecastSuccess\":true,\"workflowActivityRules\":[null]}}")]
    public void MissingOrFailedForecastDoesNotClaimNoSelectionNodes(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Null(DingTalkForecastAnalysis.Analyze(document.RootElement).HasSelfSelectedNodes);
    }

    [Fact]
    public void ReturnsSelectionNodeNamesInOrder()
    {
        using var document = JsonDocument.Parse("""
            {"result":{"isForecastSuccess":true,"workflowActivityRules":[
            {"activityType":"target_select","activityName":"部门审批"},
            {"activityType":"target_approval","activityName":"固定审批"},
            {"isTargetSelect":true,"activityName":""}]}}
            """);
        Assert.Equal(["部门审批", "未命名节点"], DingTalkForecastAnalysis.Analyze(document.RootElement).NodeNames);
    }
}
