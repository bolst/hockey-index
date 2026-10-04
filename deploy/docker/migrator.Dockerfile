FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN dotnet tool restore \
    && dotnet restore HockeyIndex.sln
RUN case "${TARGETARCH:-$(dpkg --print-architecture)}" in \
      arm64) rid=linux-arm64 ;; \
      *) rid=linux-x64 ;; \
    esac \
    && dotnet ef migrations bundle \
      --project HockeyIndex.Api \
      --configuration Release \
      --self-contained -r "$rid" \
      --output /out/efbundle \
      --force

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
WORKDIR /app
COPY --from=build /out/efbundle .
USER $APP_UID
ENTRYPOINT ["/bin/sh", "-c", "exec ./efbundle --connection \"$MIGRATOR_CONNECTION\""]
