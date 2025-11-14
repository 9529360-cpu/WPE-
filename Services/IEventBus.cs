using System;

namespace 币安量化机器人.Services
{
    public interface IEventBus
    {
        void Publish<T>(T message);
        void Subscribe<T>(Action<T> handler);
    }
}
