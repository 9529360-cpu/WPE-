using System;
using Xunit;
using 币安量化机器人.Services;
using NSubstitute;

namespace 币安量化机器人.Tests;

public class BinanceStreamClientTests
{
    [Theory]
    [InlineData("{}")]                         // 缺字段
    [InlineData("{ \"e\": \"kline\" }")]       // 半截
    [InlineData("not even json")]             // 完全错误
    public void HandleMessage_Should_NotThrow_On_MalformedJson(string raw)
    {
        var recorder = Substitute.For<IRawStreamRecorder>();
        var client = new BinanceStreamClient(recorder);

        var ex = Record.Exception(() => client.HandleMessage(raw));

        Assert.Null(ex);
        recorder.Received().Record(Arg.Any<string>(), Arg.Any<string>());
    }
}
