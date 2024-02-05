# Authentication Server with IdentityServer4

Excibition project made for the company Indice.

## Description

Authentication provider that was made using ASP.Net Core and .Net 8 SDK. Tt uses IdentityServer4 with ASP.NetIdentity for user management and a Postgres database. It was made as an MVC project to display authentication flow UI, and it also uses the entity framework as an ORM.

## Deployment

The application is deployed on a virtual machine (VM) using Docker. The PostgreSQL database used for this application is hosted on aiven.io.

## Installation

To run this application locally, please follow the steps below:

1. Clone the repository
2. Install the packages
3. Add your Database Connection String on the `appsettings.json`, or setup InMemory database
4. Edit the `Config.cs` with your Client, Scopes, and Resources
5. Run the Migrations
6. Build:
7. Run

## Contribution

Contributions are welcome! If you have any suggestions, bug reports, or improvements, please open an issue or submit a pull request.
