# Both build and runtime use the upstream Alpine image, not Microsoft images.
ARG ALPINE_VERSION=3.23
FROM alpine:${ALPINE_VERSION} AS base
RUN apk add --no-cache aspnetcore10-runtime icu-data-full tzdata krb5 \
    iputils traceroute nano
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_RUNNING_IN_CONTAINER=true
WORKDIR /app
EXPOSE 8080

FROM base AS build
# apk downloads the .NET SDK during image build; it stays out of the final image.
RUN apk add --no-cache dotnet10-sdk
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY Directory.Build.props ./
COPY src/shared/Em.Libs/ src/shared/Em.Libs/
COPY src/backend/Em.Api.Core/ src/backend/Em.Api.Core/
COPY src/modules/Em.Test/Em.Test.Models/ src/modules/Em.Test/Em.Test.Models/
COPY src/modules/Em.Test/Em.Test.Api/ src/modules/Em.Test/Em.Test.Api/
COPY src/backend/Em.Api/ src/backend/Em.Api/
RUN dotnet publish src/backend/Em.Api/Em.Api.csproj \
    --configuration "$BUILD_CONFIGURATION" --output /publish \
    /p:UseAppHost=false

FROM base AS final
RUN addgroup -S -g 1654 em && adduser -S -D -u 1654 -G em em \
    && mkdir -p /app/data && chown em:em /app/data
COPY --from=build /publish/ ./
USER em
ENTRYPOINT ["dotnet", "Em.Api.dll"]
