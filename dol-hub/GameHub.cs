﻿namespace dol_hub;

using dol_sdk.POCOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Services;

[Authorize]
public class GameHub : Hub<IGameClient>
{
    private readonly ISessionService _sessionService;
    private readonly IPlayerService _playerService;

    public GameHub(ISessionService sessionService, IPlayerService playerService)
    {
        _sessionService = sessionService;
        _playerService = playerService;
    }

    public override async Task OnConnectedAsync()
    {
        var user = this.Context.User ?? throw new ArgumentNullException(nameof(this.Context.User));
        //Find a session or create a new one
        var userId = user.Claims.First(c => c.Type == "user_id").Value;
        var player = await _playerService.GetPlayer(userId);

        ISession session;

        if (string.IsNullOrWhiteSpace(player.SessionId))
        {
            session = await _sessionService.NewSession(player);
        }
        else
        {
            session = await _sessionService.GetSession(player.SessionId) ?? await _sessionService.NewSession(player);
        }

        player.ConnectionId = Context.ConnectionId;
        await _playerService.UpdatePlayer(userId, player);
        session.Players = session.Players.Where(x => x.UserId != player.UserId).Append(player).ToArray();
        await _sessionService.Upsert(session);
        
        await Groups.AddToGroupAsync(Context.ConnectionId, session.ID);
        await base.OnConnectedAsync();

        await Clients.Group(session.ID).GameUpdate(session);
    }



    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var user = this.Context.User ?? throw new ArgumentNullException(nameof(this.Context.User));
        var userId = user.Claims.First(c => c.Type == "user_id").Value;
        var player = await _playerService.GetPlayer(userId);
        var session = await _sessionService.GetSession(player.SessionId);
        //If game is complete delete it
        if (session is not null)
        {
            session.Players = session.Players.Where(x => x.UserId != player.UserId).ToArray();
            
            if (session.Players.Count == 0)
            {
                await _sessionService.Terminate(session.ID);
            }
            else
            {
                await _sessionService.Upsert(session);
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, session.ID);
            await Clients.Group(session.ID).GameUpdate(session);
        }

        await base.OnDisconnectedAsync(exception);
    }
}

public interface IGameClient
{
    Task GameUpdate(ISession? session);
}
