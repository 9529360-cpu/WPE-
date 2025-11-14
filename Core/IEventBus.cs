using System;

namespace 币安量化机器人.Core
{
    /// <summary>
    /// 简单的内存事件总线，支持基于类型的发布/订阅。
    /// 该接口用于进程内消息传递，便于解耦 MarketDataService 与其它服务/策略。
    /// </summary>
    public interface IEventBus
    {
        void Publish<T>(T @event);
        IDisposable Subscribe<T>(Action<T> handler);
    }
}
