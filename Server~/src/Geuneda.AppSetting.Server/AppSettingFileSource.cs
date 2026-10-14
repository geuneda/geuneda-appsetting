using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geuneda.AppSetting.Server
{
	/// <summary>
	/// 서명된 문서 파일을 읽어 검증한 뒤 메모리에 두고 내려줍니다. 파일은 1초에 한 번까지 다시 읽어 내용이 바뀌었으면 검증 후 바꿉니다.
	/// </summary>
	/// <remarks>
	/// 바뀐 파일이 검증에 실패하거나(서명, 본문) 판이 지금보다 낮으면 지금 문서를 계속 내려주고 오류 로그를 한 번 남깁니다.
	/// 파일 크기·시각이 아니라 내용으로 비교하므로 같은 크기의 파일을 같은 초에 바꿔도 놓치지 않습니다.
	/// </remarks>
	internal sealed class AppSettingFileSource
	{
		internal static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

		private readonly AppSettingServerOptions _options;
		private readonly string _path;
		private readonly TimeProvider _time;
		private readonly ILogger<AppSettingFileSource> _logger;
		private readonly object _gate = new object();

		private AppSettingSnapshot _current;
		private byte[] _rejected;
		private bool _readFailed;
		private DateTimeOffset _nextCheck;

		public AppSettingFileSource(IOptions<AppSettingServerOptions> options, IHostEnvironment environment, TimeProvider time, ILogger<AppSettingFileSource> logger)
		{
			_options = options.Value;
			_options.Validate();
			_path = Path.GetFullPath(_options.DocumentPath, environment.ContentRootPath);
			_time = time;
			_logger = logger;
		}

		public string CacheControl => _options.CacheControl;

		/// <summary>처음 읽기입니다. 읽거나 검증하지 못하면 시작을 막습니다.</summary>
		/// <exception cref="InvalidOperationException">파일이 없거나 문서가 맞지 않는 경우</exception>
		public void Load()
		{
			lock (_gate)
			{
				if (_current != null)
				{
					return;
				}

				byte[] bytes;

				try
				{
					bytes = File.ReadAllBytes(_path);
				}
				catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
				{
					throw new InvalidOperationException($"Cannot read app setting document '{_path}': {exception.Message}", exception);
				}

				try
				{
					_current = Snapshot(bytes);
				}
				catch (AppSettingDocumentException exception)
				{
					throw new InvalidOperationException($"App setting document '{_path}' is invalid ({exception.Code}): {exception.Message}", exception);
				}

				_nextCheck = _time.GetUtcNow() + CheckInterval;
				_logger.LogInformation("Serving app setting revision {Revision} from {Path}", _current.Revision, _path);
			}
		}

		/// <summary>지금 내려줄 문서입니다. 확인 간격이 지났으면 파일을 다시 읽습니다.</summary>
		public AppSettingSnapshot GetCurrent()
		{
			lock (_gate)
			{
				if (_current == null)
				{
					throw new InvalidOperationException("Call Load() (MapGeunedaAppSetting) before serving");
				}

				var now = _time.GetUtcNow();

				if (now >= _nextCheck)
				{
					_nextCheck = now + CheckInterval;
					CheckFile();
				}

				return _current;
			}
		}

		private void CheckFile()
		{
			byte[] bytes;

			try
			{
				bytes = File.ReadAllBytes(_path);
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				if (!_readFailed)
				{
					_readFailed = true;
					_logger.LogError(exception, "Cannot read app setting document {Path}; still serving revision {Revision}", _path, _current.Revision);
				}

				return;
			}

			_readFailed = false;

			if (bytes.AsSpan().SequenceEqual(_current.Bytes) || (_rejected != null && bytes.AsSpan().SequenceEqual(_rejected)))
			{
				return;
			}

			AppSettingSnapshot next;

			try
			{
				next = Snapshot(bytes);
			}
			catch (AppSettingDocumentException exception)
			{
				_rejected = bytes;
				_logger.LogError("Rejected app setting document {Path} ({Code}: {Message}); still serving revision {Revision}",
					_path, exception.Code, exception.Message, _current.Revision);
				return;
			}

			if (next.Revision < _current.Revision)
			{
				_rejected = bytes;
				_logger.LogError("Rejected app setting document {Path}: revision {Next} is lower than the served {Revision} (clients ignore lower revisions)",
					_path, next.Revision, _current.Revision);
				return;
			}

			_current = next;
			_rejected = null;
			_logger.LogInformation("Serving app setting revision {Revision} from {Path}", next.Revision, _path);
		}

		private AppSettingSnapshot Snapshot(byte[] bytes)
		{
			var document = AppSettingSigner.Verify(bytes, _options.PublicKeys);
			var etag = "\"" + Base64Url.Encode(SHA256.HashData(bytes)) + "\"";
			return new AppSettingSnapshot(bytes, etag, document.Revision);
		}
	}

	/// <summary>내려줄 문서 바이트와 ETag 입니다.</summary>
	internal sealed class AppSettingSnapshot
	{
		public byte[] Bytes { get; }
		public string ETag { get; }
		public long Revision { get; }

		public AppSettingSnapshot(byte[] bytes, string etag, long revision)
		{
			Bytes = bytes;
			ETag = etag;
			Revision = revision;
		}
	}
}
