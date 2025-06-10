# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:9.0-noble AS base
RUN apt-get update \
    && apt-get install -y \
        software-properties-common \
        libc6-dev \
        libx11-dev \
        libicu-dev \
        libharfbuzz0b \
        libfreetype6 \
        libgdiplus \
        libfontconfig1 \
        fontconfig \
        libpango-1.0-0 \
        libpangocairo-1.0 \
        libpthread-stubs0-dev \
        python3 \
        python3-pip \
        python-is-python3 \
        curl \
        wget \
    && curl -sL https://deb.nodesource.com/setup_24.x | bash - \
    && apt-get install -y nodejs \
    && curl -LsSf https://astral.sh/uv/install.sh | sh \
    && apt-get clean \
    && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["Pacos/Pacos.csproj", "Pacos/"]
RUN dotnet restore "./Pacos/Pacos.csproj"
COPY . .
WORKDIR "/src/Pacos"
RUN dotnet build "./Pacos.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Pacos.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Pacos.dll"]
