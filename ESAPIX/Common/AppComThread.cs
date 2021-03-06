#region
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace ESAPIX.Common
{
    public class AppComThread : IDisposable
    {
        BlockingCollection<Task> _jobs = new BlockingCollection<Task>();
        private static AppComThread instance = null;
        private static readonly object padlock = new object();
        private Thread thread;
        private StandAloneContext _sac;
        CancellationTokenSource cts;

        private AppComThread()
        {
            cts = new CancellationTokenSource();
            thread = new Thread(() =>
            {
                foreach (var job in _jobs.GetConsumingEnumerable(cts.Token))
                {
                    try
                    {
                        job.RunSynchronously();
                    }
                    catch(Exception e)
                    {
                        _sac?.Logger.Error(e);
                    }

                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public static AppComThread Instance
        {
            get
            {
                lock (padlock)
                {
                    if (instance == null)
                    {
                        instance = new AppComThread();
                    }
                    return instance;
                }
            }
        }

        public void SetContext(Func<VMS.TPS.Common.Model.API.Application> createAppFunc)
        {
            Invoke(new Action(() =>
            {
                var app = createAppFunc();
                _sac = new StandAloneContext(app);
                _sac.Thread = this;
            }));
        }

        public T GetValue<T>(Func<StandAloneContext, T> sacFunc)
        {
            T toReturn = default(T);
            Invoke(() =>
            {
                toReturn = sacFunc(_sac);
            });
            return toReturn;
        }

        public async Task<T> GetValueAsync<T>(Func<StandAloneContext, T> sacFunc)
        {
            T toReturn = default(T);
            await InvokeAsync(() =>
            {
                toReturn = sacFunc(_sac);
            });
            return toReturn;
        }

        public void Execute(Action<StandAloneContext> sacOp)
        {
            Invoke(() =>
            {
                sacOp(_sac);
            });
        }

        public Task ExecuteAsync(Action<StandAloneContext> sacOp)
        {
            return InvokeAsync(() =>
            {
                sacOp(_sac);
            });
        }

        public Task InvokeAsync(Action action)
        {
            var task = new Task(action);
            _jobs.Add(task);
            return task;
        }

        public void Invoke(Action action)
        {
            var task = new Task(action);
            _jobs.Add(task);
            task.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Invoke(new Action(() =>
            {
                if (_sac != null)
                {
                    _sac.Application?.Dispose();
                    _sac = null;
                }
            }));

            cts.Cancel();
        }

        public int ThreadId => thread.ManagedThreadId;
    }
}