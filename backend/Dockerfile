FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/ src/
RUN --mount=type=secret,id=sixlabors_lic,required=true \
    SIXLABORS_LICENSE_KEY="$(cat /run/secrets/sixlabors_lic)" \
    dotnet publish src/Tailoring.Api/Tailoring.Api.csproj -c Release -o /out
    
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tailoring.Api.dll"]
