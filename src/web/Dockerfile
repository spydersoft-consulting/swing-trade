#Depending on the operating system of the host machines(s) that will build or run the containers, the image specified in the FROM statement may need to be changed.
#For more information, please see https://aka.ms/containercompat
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
LABEL org.opencontainers.image.source=https://github.com/spydersoft-consulting/swing-trade-web
LABEL org.opencontainers.image.description="Swing Trade Web"
LABEL org.opencontainers.image.licenses=MIT


WORKDIR /app
COPY . /app

# Serve on port 8080, we cannot serve on port 80 with a custom user that is not root.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "Spydersoft.SwingTrade.Frontend.dll"]
