FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy project files first for layer caching.
COPY TsvdChain.Core/TsvdChain.Core.csproj TsvdChain.Core/
COPY TsvdChain.P2P/TsvdChain.P2P.csproj TsvdChain.P2P/
COPY TsvdChain.Api/TsvdChain.Api.csproj TsvdChain.Api/
RUN dotnet restore TsvdChain.Api/TsvdChain.Api.csproj

# Copy everything and publish.
COPY . .
RUN dotnet publish TsvdChain.Api/TsvdChain.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app
COPY --from=build /app .

# Data volume for chain.json and wallet.json persistence.
VOLUME /app/Data

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "TsvdChain.Api.dll"]
