using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Http;
using FakeApi.Server.AspNetCore.Models;

namespace FakeApi.Server.AspNetCore.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly ConcurrentDictionary<string, User> _users = new ConcurrentDictionary<string, User>();

        private readonly Config _config;

        private Task _cleanup;

        private bool _runCleanup = true;

        public UserRepository() : this(new Config())
        {
        }
        
        public UserRepository(Config config)
        {
            _config = config ?? new Config();
            if (_config.ManualClock)
            {
                _runCleanup = false;
            }
        }

        public User Register(UserInfo userInfo)
        {
            return Register(userInfo, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        public User Register(UserInfo userInfo, long nowMs)
        {
            EnsureCleanupTaskIsStarted();
            
            var user = CreateUser();

            while (user == null)
            {
                user = CreateUser();
            }

            user.Password = _config.GuidGenerator();
            user.ExternalId = userInfo.ExternalId ?? string.Empty;
            user.LastActivityAtMs = nowMs;

            return user;
        }

        public User Get(string username)
        {
            if (_users.TryGetValue(username, out var user))
            {
                return user;
            }

            return null;
        }

        public Task<User> Authenticate(string username, string password)
        {
            return Authenticate(username, password, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        public Task<User> Authenticate(string username, string password, long nowMs)
        {
            var user = Get(username);

            if (user == null || user.Password != password)
            {
                return Task.FromResult<User>(null);
            }

            user.LastActivityAtMs = nowMs;
            return Task.FromResult(user);
        }

        public void Purge(long nowMs)
        {
            foreach (var pair in _users)
            {
                if (pair.Value.LastActivityAtMs < nowMs - _config.MaxIdle.Ticks / TimeSpan.TicksPerMillisecond)
                {
                    _users.TryRemove(pair.Key, out _);
                }
            }
        }

        public async Task StopCleanup()
        {
            _runCleanup = false;
            await _cleanup;
        }
        
        private void EnsureCleanupTaskIsStarted()
        {
            var inactiveStates = new List<TaskStatus>
            {
                TaskStatus.Faulted,
                TaskStatus.Canceled,
                TaskStatus.RanToCompletion,
                TaskStatus.WaitingForChildrenToComplete,
            };
                
            if (_cleanup != null && inactiveStates.Contains(_cleanup.Status) == false)
            {
                return;
            }

            _cleanup = Task.Run(ManageCleanup);
        }

        private async Task ManageCleanup()
        {
            while (_runCleanup)
            {
                await DoCleanup();
            }
        }
        
        private async Task DoCleanup()
        {
            await Task.Delay(_config.CleanupInterval).ConfigureAwait(false);

            foreach (var pair in _users)
            {
                if (pair.Value.LastActivityAtMs < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _config.MaxIdle.Ticks / TimeSpan.TicksPerMillisecond)
                {
                    _users.TryRemove(pair.Key, out _);
                }
            }
        }

        private User CreateUser(string preferredUsername = null)
        {
            var username = preferredUsername ?? _config.GuidGenerator();
            
            var user = new User
            {
                Username = username,
            };

            return _users.TryAdd(username, user) ? user : null;
        }
        
        public class Config
        {
            public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(15);

            public TimeSpan MaxIdle { get; set; } = TimeSpan.FromHours(2);

            public bool ManualClock { get; set; }

            public Func<string> GuidGenerator { get; set; } = () => Guid.NewGuid().ToString();

            public static Config FromEnvironment()
            {
                var clock = ServerClock.FromEnvironment();
                return new Config
                {
                    ManualClock = clock.Manual,
                    MaxIdle = TimeSpan.FromMilliseconds(clock.MaxIdleMs),
                    CleanupInterval = TimeSpan.FromMilliseconds(clock.PurgeIntervalMs),
                };
            }
        }
    }
}