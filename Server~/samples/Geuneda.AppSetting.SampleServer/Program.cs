using System;
using System.IO;
using Geuneda.AppSetting;
using Geuneda.AppSetting.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

// 실행: dotnet run --project "Server~/samples/Geuneda.AppSetting.SampleServer" --no-launch-profile -- --urls http://127.0.0.1:5399
// Unity 상호 운용 테스트: GEUNEDA_APPSETTING_INTEROP_URL=http://127.0.0.1:5399
// 샘플은 시작할 때마다 새 서명 키를 만들어 appsetting.payload.json 을 서명한다. 운영에서는 서명 CLI 로 만든 파일과 공개 키 파일을 둔다.
// 샘플 전용 엔드포인트(운영 서버에 두지 말 것): GET /sample/public-keys(공개 키), POST /sample/publish(본문 = 새 설정 JSON 을 서명해 문서 교체)
var builder = WebApplication.CreateBuilder(args);

var signingKey = AppSettingSigningKey.Generate("sample");
var folder = Directory.CreateTempSubdirectory("geuneda-appsetting-sample");
var documentPath = Path.Combine(folder.FullName, "appsetting.json");
var payload = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "appsetting.payload.json"));
File.WriteAllBytes(documentPath, AppSettingSigner.Sign(payload, new[] { signingKey }));

builder.Services.AddGeunedaAppSetting(options =>
{
	options.DocumentPath = documentPath;
	options.PublicKeys.Add(signingKey.PublicKey);
});

var app = builder.Build();
app.MapGeunedaAppSetting("/appsetting");

app.MapGet("/sample/public-keys", () => Results.Text(AppSettingPublicKeys.ToJson(new[] { signingKey.PublicKey }), "application/json"));

app.MapPost("/sample/publish", async (HttpRequest request) =>
{
	using var body = new MemoryStream();
	await request.Body.CopyToAsync(body);

	try
	{
		var document = AppSettingSigner.Sign(body.ToArray(), new[] { signingKey });
		var temporary = documentPath + ".tmp";
		await File.WriteAllBytesAsync(temporary, document);
		File.Move(temporary, documentPath, true);
		return Results.Ok(new { revision = AppSettingSigner.PeekRevision(document) });
	}
	catch (AppSettingDocumentException exception)
	{
		return Results.BadRequest(new { code = exception.Code, message = exception.Message });
	}
});

app.Run();
