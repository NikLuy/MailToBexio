FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/MailToBexio/MailToBexio.csproj", "MailToBexio/"]
RUN dotnet restore "MailToBexio/MailToBexio.csproj"
COPY src/MailToBexio/ MailToBexio/
RUN dotnet publish "MailToBexio/MailToBexio.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MailToBexio.dll"]
