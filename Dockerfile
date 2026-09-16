# syntax=docker/dockerfile:1

# Keep the SDK in the feature band selected by global.json.
FROM mcr.microsoft.com/dotnet/sdk:10.0.301-noble AS publish
WORKDIR /src
COPY global.json ./
COPY VideoNote.Server/VideoNote.Server.csproj VideoNote.Server/
COPY VideoNote.Client/VideoNote.Client.csproj VideoNote.Client/
COPY VideoNote.Shared/VideoNote.Shared.csproj VideoNote.Shared/
RUN dotnet restore VideoNote.Server/VideoNote.Server.csproj
COPY VideoNote.Server/ VideoNote.Server/
COPY VideoNote.Client/ VideoNote.Client/
COPY VideoNote.Shared/ VideoNote.Shared/
RUN dotnet publish VideoNote.Server -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app

# Install FFmpeg and its shared libraries in the final image, including FFprobe.
# Fail the image build if the codecs or packet statistics needed by VideoNote are missing.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && ffmpeg -hide_banner -loglevel error -nostdin \
        -f lavfi -i color=size=16x16:rate=1 \
        -f lavfi -i anullsrc=r=16000:cl=mono -t 1 \
        -c:v libx264 -pix_fmt yuv420p -c:a aac \
        -stats_mux_pre:v:0 /tmp/ffmpeg-stats -stats_mux_pre_fmt:v:0 '{n}' \
        /tmp/ffmpeg-check.mp4 \
    && test -s /tmp/ffmpeg-stats \
    && ffprobe -v error -show_entries stream=codec_name /tmp/ffmpeg-check.mp4 \
    && rm -f /tmp/ffmpeg-check.mp4 /tmp/ffmpeg-stats \
    && rm -rf /var/lib/apt/lists/*

COPY --from=publish /out/ ./
RUN mkdir -p /app/work && chown app:app /app/work

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Storage__RootPath=work \
    ConnectionStrings__VideoNote="Data Source=videonote.db" \
    Ffmpeg__FfmpegPath=/usr/bin/ffmpeg \
    Ffmpeg__FfprobePath=/usr/bin/ffprobe

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "VideoNote.Server.dll"]
