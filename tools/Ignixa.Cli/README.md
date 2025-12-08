# Ignixa CLI

A command-line tool for managing and testing Ignixa FHIR servers.

## Installation

Install as a .NET global tool:

```bash
dotnet tool install --global Ignixa.Cli
```

Or install locally in a project:

```bash
dotnet tool install Ignixa.Cli
```

## Getting Help

Use the built-in help command to discover available options:

```bash
# General help
ignixa help

# Command-specific help
ignixa push --help
ignixa search --help
ignixa job --help
ignixa job import --help
ignixa job export --help
ignixa job list --help
```

## Usage

### Push Resources or Bundles

Push a FHIR resource or bundle to the server:

```bash
# Push to a specific tenant
ignixa push --url http://localhost:5000 --file mybundle.json --tenant 1

# Push in single-tenant scenario (tenant parameter optional)
ignixa push --url http://localhost:5000 --file patient.json
```

**Options:**
- `--url` or `-u` - **Required** - URL of the Ignixa server
- `--file` or `-f` - **Required** - Path to the FHIR resource or bundle file
- `--tenant` or `-t` - Tenant ID (optional in single-tenant scenarios)

### Search for Resources

Search for FHIR resources using arbitrary search parameters:

```bash
# Search for patients with specific criteria
ignixa search patient --url http://localhost:5000 --firstname=bob --surname=smith

# Search in a specific tenant
ignixa search patient --url http://localhost:5000 --tenant 1 --birthdate=1990-01-01

# Search for observations
ignixa search observation --url http://localhost:5000 --code=8867-4
```

**Arguments:**
- `resourceType` - **Required** - The FHIR resource type to search (e.g., Patient, Observation)

**Options:**
- `--url` or `-u` - URL of the Ignixa server (default: http://localhost:5000)
- `--tenant` or `-t` - Tenant ID (optional in single-tenant scenarios)
- Any additional `--parameter=value` pairs are passed as search parameters

### Manage Import Jobs

Start an import job to load data from blob storage:

```bash
ignixa job import --input "fileinblob.json" --tenant 1

# With custom server URL
ignixa job import --url http://myserver:5000 --input "data/patients.ndjson" --tenant 1
```

**Options:**
- `--input` or `-i` - **Required** - Input file path in blob storage
- `--tenant` or `-t` - **Required** - Tenant ID
- `--url` or `-u` - URL of the Ignixa server (default: http://localhost:5000)

### Manage Export Jobs

Start an export job to extract data:

```bash
# Export all resources
ignixa job export --tenant 1

# Export specific resource types
ignixa job export --type "Patient,Observation" --tenant 1

# Export with type filter
ignixa job export --type "Patient" --typefilter "surname=smith" --tenant 1

# Export with SQL on FHIR view definition
ignixa job export --viewdefinition "MySqlOnFhirView" --tenant 1
```

**Options:**
- `--tenant` or `-t` - **Required** - Tenant ID
- `--type` - Resource types to export (comma-separated)
- `--typefilter` - Type filters for export
- `--viewdefinition` - SQL on FHIR view definition for export
- `--url` or `-u` - URL of the Ignixa server (default: http://localhost:5000)

### List Jobs

List all import/export jobs:

```bash
# List all jobs
ignixa job list

# List jobs for specific tenant
ignixa job list --tenant 1

# Filter by job type
ignixa job list --type Import

# Filter by status
ignixa job list --status Running
```

**Options:**
- `--tenant` or `-t` - Tenant ID (optional in single-tenant scenarios)
- `--url` or `-u` - URL of the Ignixa server (default: http://localhost:5000)
- `--type` - Filter by job type (Import or Export)
- `--status` - Filter by status (Queued, Running, Completed, Failed, Cancelled)

## Examples

```bash
# Push a patient bundle to the server
ignixa push -u http://localhost:5000 -f ./data/patient-bundle.json -t 1

# Search for patients named "Bob Smith"
ignixa search patient --firstname=bob --surname=smith

# Search in a specific tenant
ignixa search patient --tenant 1 --birthdate=gt1990-01-01

# Start an import job
ignixa job import -i "import-data.ndjson" -t 1

# Start an export job for patients
ignixa job export --type "Patient" -t 1

# Export with a view definition
ignixa job export --type "Patient" --viewdefinition "PatientSummaryView" -t 1

# List all jobs
ignixa job list
```

## Output

- Commands output JSON responses in a pretty-printed format
- Search results show total counts and individual entries
- Job commands return job IDs and status URLs for tracking

## More Information

Visit the [Ignixa FHIR repository](https://github.com/brendankowitz/ignixa-fhir) for more information.
