public class NotificationHub : Hub
{
	private YapieeContext _db { get; }
	private readonly IStringLocalizer<Resource> _localizer;
	private readonly IShared _sharedRepository;

	public NotificationHub(YapieeContext db, IStringLocalizer<Resource> localizer, IShared sharedRepository)
	{
		_db = db;
		_localizer = localizer;
		_sharedRepository = sharedRepository;
	}
//Make the connection between client and server
	public override async Task OnConnectedAsync()
	{
		var user = Context.GetHttpContext().UserProfile();
		if (user != null)
		{
			await Groups.AddToGroupAsync(Context.ConnectionId, user.Id);
			await _db.SingleInsertAsync(new UserSocket()
			{
				AspNetUserId = user.Id,
				SocketId = Context.ConnectionId,
			});
			var dbUser = await _db.AppUsers.FirstOrDefaultAsync(z => z.Id == user.Id);
			if (string.IsNullOrWhiteSpace(dbUser.Status) || dbUser.Status == UserStatus.Offline.ToString())
			{
				dbUser.Status = UserStatus.Online.ToString();
				await _db.SingleUpdateAsync(dbUser);
			}
		}
		await base.OnConnectedAsync();
	}
////disconnect connection
	public override async Task OnDisconnectedAsync(Exception? exception)
	{
		var user = Context.GetHttpContext().UserProfile();
		if (user != null)
		{
			await Groups.RemoveFromGroupAsync(Context.ConnectionId, user.Id);
			_db.UserSocket.RemoveRange(_db.UserSocket.AsNoTracking().Where(c => c.SocketId == Context.ConnectionId));
			await _db.SaveChangesAsync();
			var dbUser = await _db.AppUsers.Include(z => z.UserSockets).FirstOrDefaultAsync(z => z.Id == user.Id);
			if ((!dbUser.UserSockets.Any() && dbUser.Status == UserStatus.Online.ToString()) || string.IsNullOrWhiteSpace(dbUser.Status))
			{
				dbUser.Status = UserStatus.Offline.ToString();
				await _db.SingleUpdateAsync(dbUser);
			}
		}
		await base.OnDisconnectedAsync(exception);
	}
	public static async Task Send(IHubContext<Hub> hubContext, IList<UserProfile> getAdminUsers, YapieeContext db)
	{
		if (hubContext.Clients != null)
		{
			var userIds = getAdminUsers.Select(z => z.Id);
			var notifications = await db.NotificationUser.Include(z => z.Notification)
										  .Where(z => userIds.Contains(z.NotifyTo))
											.GetNotificationResponse(Static.Settings.DefaultCulture);

			foreach (var userId in userIds)
			{
				var notificationList = notifications.Where(z => z.NotifyTo == userId).ToList();
				if (notificationList != null && notificationList.Any())
				{
					await hubContext.Clients.Group(userId).SendAsync(WebSocketActions.Notify, notificationList);
				}
			}
		}
	}
}
