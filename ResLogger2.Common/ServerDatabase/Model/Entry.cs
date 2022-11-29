namespace ResLogger2.Common.ServerDatabase.Model;

public class Entry
{
	public uint IndexId { get; set; }
	public GameVersion FirstSeen { get; set; }
	public GameVersion LastSeen { get; set; }
	
	public bool UpdateSeen(GameVersion gv)
	{
		var changed = false;

		if (gv < FirstSeen)
		{
			FirstSeen = gv;
			changed = true;
		}

		if (gv > LastSeen)
		{
			LastSeen = gv;
			changed = true;
		}
		
		return changed;
	}
}