using System;
using System.Threading.Tasks;

namespace MazeParty.Multiplayer
{
    internal enum ApplicationQuitPreparationResult : byte
    {
        Completed = 0,
        TimedOut = 1,
        Failed = 2
    }

    /// <summary>
    /// Keeps the first quit request from entering Unity teardown while asynchronous
    /// session cleanup still needs a live PlayerLoop. Repeated requests join the
    /// same preparation and the next request is approved once cleanup settles.
    /// </summary>
    internal sealed class ApplicationQuitCoordinator
    {
        private bool _preparationStarted;

        public bool IsApproved { get; private set; }
        public Exception Failure { get; private set; }
        public Task<ApplicationQuitPreparationResult> PreparationTask { get; private set; }

        public bool TryBegin(Func<Task> cleanup, Task deadline)
        {
            if (_preparationStarted || IsApproved)
            {
                return false;
            }
            if (cleanup == null)
            {
                throw new ArgumentNullException(nameof(cleanup));
            }
            if (deadline == null)
            {
                throw new ArgumentNullException(nameof(deadline));
            }

            _preparationStarted = true;
            PreparationTask = PrepareAsync(cleanup, deadline);
            return true;
        }

        private async Task<ApplicationQuitPreparationResult> PrepareAsync(
            Func<Task> cleanup,
            Task deadline)
        {
            var result = ApplicationQuitPreparationResult.Completed;
            try
            {
                var cleanupTask = cleanup() ?? Task.CompletedTask;
                var completed = await Task.WhenAny(cleanupTask, deadline);
                if (!ReferenceEquals(completed, cleanupTask))
                {
                    result = ApplicationQuitPreparationResult.TimedOut;
                }
                else
                {
                    await cleanupTask;
                }
            }
            catch (Exception exception)
            {
                Failure = exception;
                result = ApplicationQuitPreparationResult.Failed;
            }

            IsApproved = true;
            return result;
        }
    }
}
