FROM mcr.microsoft.com/dotnet/aspnet:10.0

LABEL org.opencontainers.image.source=https://github.com/spydersoft-consulting/swing-trade-api
LABEL org.opencontainers.image.description="Swing Trade API"
LABEL org.opencontainers.image.licenses=MIT

# The API uses the IANA zone America/New_York (see docs/DECISIONS.md #16); this base image ships tzdata.
WORKDIR /app
COPY . /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "Spydersoft.SwingTrade.Api.dll"]
