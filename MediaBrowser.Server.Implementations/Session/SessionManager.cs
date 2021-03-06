﻿using MediaBrowser.Common.Events;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Server.Implementations.Session
{
    /// <summary>
    /// Class SessionManager
    /// </summary>
    public class SessionManager : ISessionManager
    {
        /// <summary>
        /// The _user data repository
        /// </summary>
        private readonly IUserDataRepository _userDataRepository;

        /// <summary>
        /// The _user repository
        /// </summary>
        private readonly IUserRepository _userRepository;

        /// <summary>
        /// The _logger
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Gets or sets the configuration manager.
        /// </summary>
        /// <value>The configuration manager.</value>
        private readonly IServerConfigurationManager _configurationManager;

        /// <summary>
        /// The _active connections
        /// </summary>
        private readonly ConcurrentDictionary<string, SessionInfo> _activeConnections =
            new ConcurrentDictionary<string, SessionInfo>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Occurs when [playback start].
        /// </summary>
        public event EventHandler<PlaybackProgressEventArgs> PlaybackStart;
        /// <summary>
        /// Occurs when [playback progress].
        /// </summary>
        public event EventHandler<PlaybackProgressEventArgs> PlaybackProgress;
        /// <summary>
        /// Occurs when [playback stopped].
        /// </summary>
        public event EventHandler<PlaybackProgressEventArgs> PlaybackStopped;

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionManager"/> class.
        /// </summary>
        /// <param name="userDataRepository">The user data repository.</param>
        /// <param name="configurationManager">The configuration manager.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="userRepository">The user repository.</param>
        public SessionManager(IUserDataRepository userDataRepository, IServerConfigurationManager configurationManager, ILogger logger, IUserRepository userRepository)
        {
            _userDataRepository = userDataRepository;
            _configurationManager = configurationManager;
            _logger = logger;
            _userRepository = userRepository;
        }

        /// <summary>
        /// Gets all connections.
        /// </summary>
        /// <value>All connections.</value>
        public IEnumerable<SessionInfo> Sessions
        {
            get { return _activeConnections.Values.OrderByDescending(c => c.LastActivityDate).ToList(); }
        }

        /// <summary>
        /// Logs the user activity.
        /// </summary>
        /// <param name="clientType">Type of the client.</param>
        /// <param name="appVersion">The app version.</param>
        /// <param name="deviceId">The device id.</param>
        /// <param name="deviceName">Name of the device.</param>
        /// <param name="user">The user.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.UnauthorizedAccessException"></exception>
        /// <exception cref="System.ArgumentNullException">user</exception>
        public async Task<SessionInfo> LogConnectionActivity(string clientType, string appVersion, string deviceId, string deviceName, User user)
        {
            if (string.IsNullOrEmpty(clientType))
            {
                throw new ArgumentNullException("clientType");
            }
            if (string.IsNullOrEmpty(appVersion))
            {
                throw new ArgumentNullException("appVersion");
            }
            if (string.IsNullOrEmpty(deviceId))
            {
                throw new ArgumentNullException("deviceId");
            }
            if (string.IsNullOrEmpty(deviceName))
            {
                throw new ArgumentNullException("deviceName");
            }

            if (user != null && user.Configuration.IsDisabled)
            {
                throw new UnauthorizedAccessException(string.Format("The {0} account is currently disabled. Please consult with your administrator.", user.Name));
            }

            var activityDate = DateTime.UtcNow;

            var session = GetSessionInfo(clientType, appVersion, deviceId, deviceName, user);
            
            session.LastActivityDate = activityDate;

            if (user == null)
            {
                return session;
            }

            var lastActivityDate = user.LastActivityDate;

            user.LastActivityDate = activityDate;

            // Don't log in the db anymore frequently than 10 seconds
            if (lastActivityDate.HasValue && (activityDate - lastActivityDate.Value).TotalSeconds < 10)
            {
                return session;
            }

            // Save this directly. No need to fire off all the events for this.
            await _userRepository.SaveUser(user, CancellationToken.None).ConfigureAwait(false);

            return session;
        }

        /// <summary>
        /// Updates the now playing item id.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="item">The item.</param>
        /// <param name="isPaused">if set to <c>true</c> [is paused].</param>
        /// <param name="currentPositionTicks">The current position ticks.</param>
        private void UpdateNowPlayingItem(SessionInfo session, BaseItem item, bool isPaused, bool isMuted, long? currentPositionTicks = null)
        {
            session.IsMuted = isMuted;
            session.IsPaused = isPaused;
            session.NowPlayingPositionTicks = currentPositionTicks;
            session.NowPlayingItem = item;
            session.LastActivityDate = DateTime.UtcNow;
        }

        /// <summary>
        /// Removes the now playing item id.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="item">The item.</param>
        private void RemoveNowPlayingItem(SessionInfo session, BaseItem item)
        {
            if (session.NowPlayingItem != null && session.NowPlayingItem.Id == item.Id)
            {
                session.NowPlayingItem = null;
                session.NowPlayingPositionTicks = null;
                session.IsPaused = false;
            }
        }

        /// <summary>
        /// Gets the connection.
        /// </summary>
        /// <param name="clientType">Type of the client.</param>
        /// <param name="appVersion">The app version.</param>
        /// <param name="deviceId">The device id.</param>
        /// <param name="deviceName">Name of the device.</param>
        /// <param name="user">The user.</param>
        /// <returns>SessionInfo.</returns>
        private SessionInfo GetSessionInfo(string clientType, string appVersion, string deviceId, string deviceName, User user)
        {
            var key = clientType + deviceId + appVersion;

            var connection = _activeConnections.GetOrAdd(key, keyName => new SessionInfo
            {
                Client = clientType,
                DeviceId = deviceId,
                ApplicationVersion = appVersion,
                Id = Guid.NewGuid()
            });

            connection.DeviceName = deviceName;
            connection.User = user;

            return connection;
        }

        /// <summary>
        /// Used to report that playback has started for an item
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="sessionId">The session id.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        public async Task OnPlaybackStart(BaseItem item, Guid sessionId)
        {
            if (item == null)
            {
                throw new ArgumentNullException();
            }

            var session = Sessions.First(i => i.Id.Equals(sessionId));

            UpdateNowPlayingItem(session, item, false, false);

            var key = item.GetUserDataKey();

            var user = session.User;
            
            var data = _userDataRepository.GetUserData(user.Id, key);

            data.PlayCount++;
            data.LastPlayedDate = DateTime.UtcNow;

            if (!(item is Video))
            {
                data.Played = true;
            }

            await _userDataRepository.SaveUserData(user.Id, key, data, CancellationToken.None).ConfigureAwait(false);

            // Nothing to save here
            // Fire events to inform plugins
            EventHelper.QueueEventIfNotNull(PlaybackStart, this, new PlaybackProgressEventArgs
            {
                Item = item,
                User = user
            }, _logger);
        }

        /// <summary>
        /// Used to report playback progress for an item
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="positionTicks">The position ticks.</param>
        /// <param name="isPaused">if set to <c>true</c> [is paused].</param>
        /// <param name="sessionId">The session id.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        /// <exception cref="System.ArgumentOutOfRangeException">positionTicks</exception>
        public async Task OnPlaybackProgress(BaseItem item, long? positionTicks, bool isPaused, bool isMuted, Guid sessionId)
        {
            if (item == null)
            {
                throw new ArgumentNullException();
            }

            if (positionTicks.HasValue && positionTicks.Value < 0)
            {
                throw new ArgumentOutOfRangeException("positionTicks");
            }

            var session = Sessions.First(i => i.Id.Equals(sessionId));

            UpdateNowPlayingItem(session, item, isPaused, isMuted, positionTicks);

            var key = item.GetUserDataKey();

            var user = session.User;

            if (positionTicks.HasValue)
            {
                var data = _userDataRepository.GetUserData(user.Id, key);

                UpdatePlayState(item, data, positionTicks.Value);
                await _userDataRepository.SaveUserData(user.Id, key, data, CancellationToken.None).ConfigureAwait(false);
            }

            EventHelper.QueueEventIfNotNull(PlaybackProgress, this, new PlaybackProgressEventArgs
            {
                Item = item,
                User = user,
                PlaybackPositionTicks = positionTicks

            }, _logger);
        }

        /// <summary>
        /// Used to report that playback has ended for an item
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="positionTicks">The position ticks.</param>
        /// <param name="sessionId">The session id.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        public async Task OnPlaybackStopped(BaseItem item, long? positionTicks, Guid sessionId)
        {
            if (item == null)
            {
                throw new ArgumentNullException();
            }

            if (positionTicks.HasValue && positionTicks.Value < 0)
            {
                throw new ArgumentOutOfRangeException("positionTicks");
            }
            
            var session = Sessions.First(i => i.Id.Equals(sessionId));

            RemoveNowPlayingItem(session, item);

            var key = item.GetUserDataKey();

            var user = session.User;
            
            var data = _userDataRepository.GetUserData(user.Id, key);

            if (positionTicks.HasValue)
            {
                UpdatePlayState(item, data, positionTicks.Value);
            }
            else
            {
                // If the client isn't able to report this, then we'll just have to make an assumption
                data.PlayCount++;
                data.Played = true;
                data.PlaybackPositionTicks = 0;
            }

            await _userDataRepository.SaveUserData(user.Id, key, data, CancellationToken.None).ConfigureAwait(false);

            EventHelper.QueueEventIfNotNull(PlaybackStopped, this, new PlaybackProgressEventArgs
            {
                Item = item,
                User = user,
                PlaybackPositionTicks = positionTicks
            }, _logger);
        }

        /// <summary>
        /// Updates playstate position for an item but does not save
        /// </summary>
        /// <param name="item">The item</param>
        /// <param name="data">User data for the item</param>
        /// <param name="positionTicks">The current playback position</param>
        private void UpdatePlayState(BaseItem item, UserItemData data, long positionTicks)
        {
            var hasRuntime = item.RunTimeTicks.HasValue && item.RunTimeTicks > 0;

            // If a position has been reported, and if we know the duration
            if (positionTicks > 0 && hasRuntime)
            {
                var pctIn = Decimal.Divide(positionTicks, item.RunTimeTicks.Value) * 100;

                // Don't track in very beginning
                if (pctIn < _configurationManager.Configuration.MinResumePct)
                {
                    positionTicks = 0;
                }

                // If we're at the end, assume completed
                else if (pctIn > _configurationManager.Configuration.MaxResumePct || positionTicks >= item.RunTimeTicks.Value)
                {
                    positionTicks = 0;
                    data.Played = true;
                }

                else
                {
                    // Enforce MinResumeDuration
                    var durationSeconds = TimeSpan.FromTicks(item.RunTimeTicks.Value).TotalSeconds;

                    if (durationSeconds < _configurationManager.Configuration.MinResumeDurationSeconds)
                    {
                        positionTicks = 0;
                        data.Played = true;
                    }
                }
            }
            else if (!hasRuntime)
            {
                // If we don't know the runtime we'll just have to assume it was fully played
                data.Played = true;
                positionTicks = 0;
            }

            if (item is Audio)
            {
                positionTicks = 0;
            }

            data.PlaybackPositionTicks = positionTicks;
        }
    }
}
