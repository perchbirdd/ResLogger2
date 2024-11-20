FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["ResLogger2.Web/ResLogger2.Web.csproj", "ResLogger2.Web/"]
RUN dotnet restore "ResLogger2.Web/ResLogger2.Web.csproj"

COPY ["ResLogger2.Common/ResLogger2.Common.csproj", "ResLogger2.Common/"]
RUN dotnet restore "ResLogger2.Common/ResLogger2.Common.csproj"

WORKDIR "/src/ResLogger2.Common"
COPY ResLogger2.Common/ .

WORKDIR "/src/ResLogger2.Web"
COPY ResLogger2.Web .
RUN dotnet build "ResLogger2.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ResLogger2.Web.csproj" -c Release -o /app/publish

FROM base AS final
ENV TZ=America/New_York
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ResLogger2.Web.dll"]
