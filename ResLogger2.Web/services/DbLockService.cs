namespace ResLogger2.Web.Services;

public class DbLockService : IDbLockService
{
	private readonly SemaphoreSlim _semaphore;
	
	public DbLockService()
	{
		_semaphore = new SemaphoreSlim(1, 1);
	}
	
	public bool AcquireLock()
	{
		return _semaphore.Wait(TimeSpan.FromSeconds(1));				
	}
	
	public bool AcquireLock(TimeSpan timeout)
	{
		return _semaphore.Wait(timeout);				
	}

	public async Task<bool> AcquireLockAsync()
	{
		return await _semaphore.WaitAsync(TimeSpan.FromSeconds(1));
	}
	
	public async Task<bool> AcquireLockAsync(TimeSpan timeout)
	{
		return await _semaphore.WaitAsync(timeout);
	}
	
	public void ReleaseLock()
	{
		_semaphore.Release();
	}
}