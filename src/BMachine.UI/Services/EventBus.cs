using BMachine.SDK;
using CommunityToolkit.Mvvm.Messaging;

namespace BMachine.UI.Services;

public class EventBus : IEventBus
{
    public void Publish<T>(T eventData) where T : class, IEvent
    {
        WeakReferenceMessenger.Default.Send(eventData);
    }

    public IDisposable Subscribe<T>(Action<T> handler) where T : class, IEvent
    {
        WeakReferenceMessenger.Default.Register<T>(this, (r, m) => handler(m));
        return new Unsubscriber<T>(this);
    }

    private class Unsubscriber<T> : IDisposable where T : class, IEvent
    {
        private readonly object _recipient;

        public Unsubscriber(object recipient)
        {
            _recipient = recipient;
        }

        public void Dispose()
        {
            WeakReferenceMessenger.Default.Unregister<T>(_recipient);
        }
    }
}
