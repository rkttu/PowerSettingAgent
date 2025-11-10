# !/usr/bin/env dotnet

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

try
{
	if (!OperatingSystem.IsWindows())
		throw new PowerSettingException("이 프로그램은 Microsoft Windows에서만 사용할 수 있습니다.", new PlatformNotSupportedException());

	// 현재 설정 확인
	Console.Out.WriteLine("# 현재 설정");
	Console.Out.WriteLine();
	Console.Out.WriteLine($"* DC: {PowerDisplaySettings.GetDisplayTimeout(PowerType.DC)?.ToString() ?? "None"}");
	Console.Out.WriteLine($"* AC: {PowerDisplaySettings.GetDisplayTimeout(PowerType.AC)?.ToString() ?? "None"}");

	// 배터리 사용 시 설정 변경
	Console.Out.WriteLine();
	Console.Out.WriteLine("# 설정 변경 시도");
	Console.Out.WriteLine();
	Console.Out.WriteLine("배터리 사용 시 디스플레이 끄기 시간을 [20분]으로 변경...");
	PowerDisplaySettings.SetDisplayTimeout(TimeSpan.FromMinutes(20d), PowerType.DC);

	// 전원 사용 시 설정 변경
	Console.Out.WriteLine("전원 사용 시 디스플레이 끄기 시간을 [None]으로 변경...");
	PowerDisplaySettings.SetDisplayTimeout(default, PowerType.AC);

	// 변경된 설정 확인
	Console.Out.WriteLine();
	Console.Out.WriteLine("# 변경된 설정");
	Console.Out.WriteLine();
	Console.Out.WriteLine($"* DC: {PowerDisplaySettings.GetDisplayTimeout(PowerType.DC)?.ToString() ?? "None"}");
	Console.Out.WriteLine($"* AC: {PowerDisplaySettings.GetDisplayTimeout(PowerType.AC)?.ToString() ?? "None"}");
}
catch (Exception ex)
{
	Console.Error.WriteLine($"오류 발생: {ex.Message}");
}

internal static class NativeMethods
{
	[DllImport("powrprof.dll")]
	public static extern uint PowerGetActiveScheme(nint UserRootPowerKey, out nint ActivePolicyGuid);

	[DllImport("powrprof.dll")]
	public static extern uint PowerWriteACValueIndex(nint RootPowerKey, ref Guid SchemeGuid,
		ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, uint AcValueIndex);

	[DllImport("powrprof.dll")]
	public static extern uint PowerWriteDCValueIndex(nint RootPowerKey, ref Guid SchemeGuid,
		ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, uint DcValueIndex);

	[DllImport("powrprof.dll")]
	public static extern uint PowerReadACValueIndex(nint RootPowerKey, ref Guid SchemeGuid,
		ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, out uint AcValueIndex);

	[DllImport("powrprof.dll")]
	public static extern uint PowerReadDCValueIndex(nint RootPowerKey, ref Guid SchemeGuid,
		ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, out uint DcValueIndex);

	public static Guid GUID_VIDEO_SUBGROUP = new Guid("7516b95f-f776-4464-8c53-06167f40cc99");
	public static Guid GUID_VIDEO_POWERDOWN_TIMEOUT = new Guid("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");

	public const uint NEVER_TIMEOUT = 0xFFFFFFFFu;  // "해당 없음" 설정값
}

public enum PowerType : int
{
	AC,
	DC,
}

internal static class PowerDisplaySettings
{
	public static TimeSpan? GetDisplayTimeout(PowerType powerType)
	{
		try
		{
			uint result = NativeMethods.PowerGetActiveScheme(default, out nint activeSchemeGuid);
			if (result != 0u)
				throw new PowerSettingException($"전원 구성표를 가져오는데 실패했습니다. 에러 코드: {result:X8}", new Win32Exception((int)result));

			Guid activeScheme = Marshal.PtrToStructure<Guid>(activeSchemeGuid);
			uint timeoutSeconds;

			// 전원 타입에 따라 다른 함수 호출
			if (powerType == PowerType.AC)
			{
				result = NativeMethods.PowerReadACValueIndex(
					default,
					ref activeScheme,
					ref NativeMethods.GUID_VIDEO_SUBGROUP,
					ref NativeMethods.GUID_VIDEO_POWERDOWN_TIMEOUT,
					out timeoutSeconds);
			}
			else
			{
				result = NativeMethods.PowerReadDCValueIndex(
					default,
					ref activeScheme,
					ref NativeMethods.GUID_VIDEO_SUBGROUP,
					ref NativeMethods.GUID_VIDEO_POWERDOWN_TIMEOUT,
					out timeoutSeconds);
			}

			Marshal.FreeHGlobal(activeSchemeGuid);

			if (result != 0u)
				throw new PowerSettingException($"설정값을 읽는데 실패했습니다. 에러 코드: {result}", new Win32Exception((int)result));

			// "해당 없음" 설정 확인
			if (timeoutSeconds == NativeMethods.NEVER_TIMEOUT)
				return default;

			return TimeSpan.FromSeconds(timeoutSeconds);
		}
		catch (Exception ex)
		{
			throw new PowerSettingException($"디스플레이 끄기 시간을 가져오는데 실패했습니다: {ex.Message}", ex);
		}
	}

	public static void SetDisplayTimeout(TimeSpan? timeoutDuration, PowerType powerType)
	{
		if (timeoutDuration.HasValue && timeoutDuration.Value.CompareTo(TimeSpan.Zero) < 0)
			throw new PowerSettingException("유효하지 않은 시간값입니다. -1(해당 없음) 또는 0 이상의 값을 입력하세요.", new ArgumentException(nameof(timeoutDuration)));

		try
		{
			var result = NativeMethods.PowerGetActiveScheme(default, out nint activeSchemeGuid);

			if (result != 0)
				throw new PowerSettingException($"전원 구성표를 가져오는데 실패했습니다. 에러 코드: {result}");

			var activeScheme = Marshal.PtrToStructure<Guid>(activeSchemeGuid);
			var timeoutSeconds = timeoutDuration.HasValue ? (uint)timeoutDuration.Value.TotalSeconds : NativeMethods.NEVER_TIMEOUT;

			// 전원 타입에 따라 다른 함수 호출
			if (powerType == PowerType.AC)
			{
				result = NativeMethods.PowerWriteACValueIndex(
					default,
					ref activeScheme,
					ref NativeMethods.GUID_VIDEO_SUBGROUP,
					ref NativeMethods.GUID_VIDEO_POWERDOWN_TIMEOUT,
					timeoutSeconds);
			}
			else
			{
				result = NativeMethods.PowerWriteDCValueIndex(
					default,
					ref activeScheme,
					ref NativeMethods.GUID_VIDEO_SUBGROUP,
					ref NativeMethods.GUID_VIDEO_POWERDOWN_TIMEOUT,
					timeoutSeconds);
			}

			Marshal.FreeHGlobal(activeSchemeGuid);

			if (result != 0)
				throw new PowerSettingException($"설정값을 변경하는데 실패했습니다. 에러 코드: {result}", new Win32Exception((int)result));
		}
		catch (Exception ex)
		{
			throw new PowerSettingException($"디스플레이 끄기 시간을 설정하는데 실패했습니다: {ex.Message}", ex);
		}
	}
}

public sealed class PowerSettingException : Exception
{
	public PowerSettingException(string message) : base(message) { }
	public PowerSettingException(string message, Exception innerException) : base(message, innerException) { }
}
