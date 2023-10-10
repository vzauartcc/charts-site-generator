# Start with the official .NET SDK image
FROM mcr.microsoft.com/dotnet/sdk:7.0 as build

# Install Node.js (for npx and other Node-based tools)
RUN curl -sL https://deb.nodesource.com/setup_14.x | bash -
RUN apt-get install -y nodejs

# Set working directory
WORKDIR /app

# Copy everything into the image
COPY . .

# Run the commands from your build script
RUN curl -sSL https://dot.net/v1/dotnet-install.sh > dotnet-install.sh && \
    chmod +x dotnet-install.sh && \
    ./dotnet-install.sh -c 7.0 -InstallDir ./dotnet && \
    ./dotnet/dotnet --version && \
    ./dotnet/dotnet publish -c Release -o output && \
    ./dotnet/dotnet ./output/charts-site-generator.dll ./output/wwwroot && \
    npx tailwindcss -i ./style.css -o ./output/wwwroot/style.css && \
    cp ./script.js ./output/wwwroot && \
    cp ./favicon.ico ./output/wwwroot

# Optional: In case you want to create a smaller runtime image, 
# you can use a multi-stage build. If not, ignore this section.
FROM mcr.microsoft.com/dotnet/aspnet:7.0 as runtime

WORKDIR /app

COPY --from=build /app/output/wwwroot /app

# The command to run when the container starts
# Replace with the command that serves your application, if necessary
CMD ["dotnet", "charts-site-generator.dll"]
