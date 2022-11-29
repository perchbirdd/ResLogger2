namespace ResLogger2.Web.Services;

public interface IDbLockService
{
	public bool AcquireLock();
	public bool AcquireLock(TimeSpan timeout);
	public Task<bool> AcquireLockAsync();
	public Task<bool> AcquireLockAsync(TimeSpan timeout);
	public void ReleaseLock();
}