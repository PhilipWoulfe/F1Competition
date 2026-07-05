#!/bin/bash
# Usage: 
#   ./build.sh            <- Normal build (Release + Tests + Docker)
#   ./build.sh --debug    <- Debug build (No Tests + Debug Config + Web Debug)
#   ./build.sh --quality  <- Quality gate only (format + CI-style builds, no Docker)
#   ./build.sh --ci       <- CI mode (quality + tests + coverage, no Docker)

# 1. Initialize variables
COMPOSE_FILES="-f docker-compose.yml"
MODE="Normal"
CONFIG="Release"

API_PROJECT="src/F1.Api/F1.Api.csproj"
WEB_PROJECT="src/F1.Web/F1.Web.csproj"
DATA_SYNC_PROJECT="src/F1.DataSyncWorker/F1.DataSyncWorker.csproj"
API_TEST_PROJECT="tests/F1.Api.Tests/F1.Api.Tests.csproj"
WEB_TEST_PROJECT="tests/F1.Web.Tests/F1.Web.Tests.csproj"
INFRA_TEST_PROJECT="tests/F1.Infrastructure.Tests/F1.Infrastructure.Tests.csproj"

FORMAT_INCLUDE_PATHS=(
  "src/F1.Api"
  "src/F1.Core"
  "src/F1.Infrastructure"
    "src/F1.DataSyncWorker"
  "src/F1.Services"
  "src/F1.Web"
  "tests/F1.Api.Tests"
  "tests/F1.Web.Tests"
)

# 2. Check for the --debug flag
case "$1" in
    --debug)
        COMPOSE_FILES="-f docker-compose.yml -f docker-compose.debug.yml"
        MODE="Debug"
        CONFIG="Debug"
        ;;
    --quality)
        MODE="Quality"
        CONFIG="Release"
        ;;
    --ci)
        MODE="CI"
        CONFIG="Release"
        ;;
esac

echo "🧹 Cleaning stale artifacts and bin/obj folders..."
# Clean local folders to prevent volume-mount pollution in Docker
# dotnet clean
find . -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} + 2>/dev/null || true
rm -rf tests/**/TestResults

run_quality_gate() {
    echo "🧪 Running quality gate (F1 projects only, PopulateF1Database excluded)..."

    if ! dotnet restore "$API_PROJECT"; then return 1; fi
    if ! dotnet restore "$WEB_PROJECT"; then return 1; fi
    if ! dotnet restore "$DATA_SYNC_PROJECT"; then return 1; fi
    if ! dotnet restore "$API_TEST_PROJECT"; then return 1; fi
    if ! dotnet restore "$WEB_TEST_PROJECT"; then return 1; fi
    if ! dotnet restore "$INFRA_TEST_PROJECT"; then return 1; fi

    if ! dotnet format whitespace F1Competition.sln --verify-no-changes --no-restore --include "${FORMAT_INCLUDE_PATHS[@]}"; then return 1; fi

    if ! CI=true dotnet build "$API_PROJECT" --configuration Release --no-restore; then return 1; fi
    if ! CI=true dotnet build "$WEB_PROJECT" --configuration Release --no-restore; then return 1; fi
    if ! CI=true dotnet build "$DATA_SYNC_PROJECT" --configuration Release --no-restore; then return 1; fi
    if ! CI=true dotnet build "$API_TEST_PROJECT" --configuration Release --no-restore; then return 1; fi
    if ! CI=true dotnet build "$WEB_TEST_PROJECT" --configuration Release --no-restore; then return 1; fi
    if ! CI=true dotnet build "$INFRA_TEST_PROJECT" --configuration Release --no-restore; then return 1; fi

    echo "✅ Quality gate passed."
}

# 3. Conditional Test Block
if [[ "$MODE" == "Debug" ]]; then
    echo "⏭️  Skipping Unit Tests for Debug Mode..."
elif [[ "$MODE" == "Quality" ]]; then
    if ! run_quality_gate; then
        printf "\033[0;31m❌ Quality gate failed.\033[0m\n"
        exit 1
    fi
    echo "🏁 Quality mode complete."
    exit 0
elif [[ "$MODE" == "CI" ]]; then
    if ! run_quality_gate; then
        printf "\033[0;31m❌ Quality gate failed in CI mode.\033[0m\n"
        exit 1
    fi

    echo "🏎️  Running API/Backend Unit Tests with Coverage (CI Mode)..."
    # Keep API coverage scoped to API/Core/Services; infrastructure coverage is validated
    # in the dedicated F1.Infrastructure.Tests project.
    if ! CI=true dotnet test "$API_TEST_PROJECT" -c "$CONFIG" --nologo --verbosity minimal \
        /p:CollectCoverage=true \
        /p:CoverletOutputFormat=cobertura \
        /p:CoverletOutput=./TestResults/api-coverage/ \
        /p:Include="[F1.Api]*%2c[F1.Core]*%2c[F1.Services]*" \
        /p:Threshold=30; then
        printf "\033[0;31m❌ API/Backend tests failed in CI mode.\033[0m\n"
        exit 1
    fi

    echo "🏎️  Running Web Unit Tests with Web-Only Coverage (CI Mode)..."
    if ! CI=true dotnet test "$WEB_TEST_PROJECT" -c "$CONFIG" --nologo --verbosity minimal \
        /p:CollectCoverage=true \
        /p:CoverletOutputFormat=cobertura \
        /p:CoverletOutput=./TestResults/web-coverage/ \
        /p:Include="[F1.Web]*" \
        /p:Threshold=30; then
        printf "\033[0;31m❌ Web tests failed in CI mode.\033[0m\n"
        exit 1
    fi

    echo "🏎️  Running Infrastructure Unit Tests with Coverage (CI Mode)..."
    if ! CI=true dotnet test "$INFRA_TEST_PROJECT" -c "$CONFIG" --nologo --verbosity minimal \
        /p:CollectCoverage=true \
        /p:CoverletOutputFormat=cobertura \
        /p:CoverletOutput=./TestResults/infra-coverage/ \
        /p:Include="[F1.Infrastructure]*" \
        /p:ExcludeByFile="**/Migrations/*.cs" \
        /p:Threshold=20; then
        printf "\033[0;31m❌ Infrastructure tests failed in CI mode.\033[0m\n"
        exit 1
    fi

    echo "✅ CI mode complete (quality + tests)."
    exit 0
else
    echo "🏎️  Running API/Backend Unit Tests with Coverage ($CONFIG Mode)..."
    # Keep API coverage scoped to API/Core/Services; infrastructure coverage is validated
    # in the dedicated F1.Infrastructure.Tests project.
    if ! dotnet test "$API_TEST_PROJECT" -c "$CONFIG" --nologo --verbosity minimal \
        /p:CollectCoverage=true \
        /p:CoverletOutputFormat=cobertura \
        /p:CoverletOutput=./TestResults/api-coverage/ \
        /p:Include="[F1.Api]*%2c[F1.Core]*%2c[F1.Services]*" \
        /p:Threshold=30; then
        printf "\033[0;31m❌ API/Backend tests failed! Aborting build.\033[0m\n"
        printf "\a"
        exit 1
    fi

    echo "🏎️  Running Web Unit Tests with Web-Only Coverage ($CONFIG Mode)..."
    if ! dotnet test "$WEB_TEST_PROJECT" -c "$CONFIG" --nologo --verbosity minimal \
        /p:CollectCoverage=true \
        /p:CoverletOutputFormat=cobertura \
        /p:CoverletOutput=./TestResults/web-coverage/ \
        /p:Include="[F1.Web]*" \
        /p:Threshold=30; then
        printf "\033[0;31m❌ Web tests failed! Aborting build.\033[0m\n"
        printf "\a"
        exit 1
    fi

    echo "🏎️  Running Infrastructure Unit Tests with Coverage ($CONFIG Mode)..."
    if ! dotnet test "$INFRA_TEST_PROJECT" -c "$CONFIG" --nologo --verbosity minimal \
        /p:CollectCoverage=true \
        /p:CoverletOutputFormat=cobertura \
        /p:CoverletOutput=./TestResults/infra-coverage/ \
        /p:Include="[F1.Infrastructure]*" \
        /p:ExcludeByFile="**/Migrations/*.cs" \
        /p:Threshold=20; then
        printf "\033[0;31m❌ Infrastructure tests failed! Aborting build.\033[0m\n"
        printf "\a"
        exit 1
    fi

    echo "✅ API, Web, and Infrastructure tests passed!"
fi

# 4. Fire up the containers
echo "🏗️  Starting Docker Build ($MODE Mode with $CONFIG configuration)..."

# We export CONFIG so docker-compose can pick it up, 
# or we pass it directly via --build-arg
export CONFIGURATION=$CONFIG

docker compose $COMPOSE_FILES up -d --build

echo "🚀 Containers are up! Mode: $MODE"
if [[ "$MODE" == "Debug" ]]; then
    echo "💡 DEBUGGER READY:"
    echo "   - API: Attach to 'f1-local' (Port 15000)"
    echo "   - Web: Launch 'Debug Blazor Web (Docker)' in VS Code"
fi