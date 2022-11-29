using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using Newtonsoft.Json.Linq;
using ResLogger2.Common.ServerDatabase.Model;

namespace ResLogger2.Web.Services;

public class ThaliakService : IThaliakService
{
	public async Task<List<LatestProcessedVersion>> GetLatestVersions()
	{
		var gql = new GraphQLHttpClient(@"https://thaliak.xiv.dev/graphql/", new NewtonsoftJsonSerializer());
		var req = new GraphQLRequest
		{
			Query = """
			{
				game: repository(slug:"4e9a232b") {
					slug
					latestVersion {
						versionString
					}
				}
				ex1: repository(slug:"6b936f08") {
					slug
					latestVersion {
						versionString
					}
				}
				ex2: repository(slug:"f29a3eb2") {
					slug
					latestVersion {
						versionString
					}
				}
				ex3: repository(slug:"859d0e24") {
					slug
					latestVersion {
						versionString
					}
				}
				ex4: repository(slug:"1bf99b87") {
					slug
					latestVersion {
						versionString
					}
				}
			}
			""",
		};
		var resp = await gql.SendQueryAsync<object>(req);
		var results = new List<LatestProcessedVersion>();
		var parse = (JObject)resp.Data;
		
		foreach (var (_, token) in parse)
		{
			var repo = token["slug"].Value<string>();
			var ver = token["latestVersion"]["versionString"].Value<string>();
			results.Add(new LatestProcessedVersion {Repo = repo, Version = GameVersion.Parse(ver) });
		}
		
		return results;
	}
	
	public async Task<List<string>> GetPatchUrls()
	{
		var gql = new GraphQLHttpClient(@"https://thaliak.xiv.dev/graphql/", new NewtonsoftJsonSerializer());
		var req = new GraphQLRequest
		{
			Query = """
			{
			  game: repository(slug:"4e9a232b") {
			    versions {
			      patches {
			        url
			      }
			    }
			  }
			  ex1: repository(slug:"6b936f08") {
			    versions {
			      patches {
			        url
			      }
			    }
			  }
			  ex2: repository(slug:"f29a3eb2") {
			    versions {
			      patches {
			        url
			      }
			    }
			  }
			  ex3: repository(slug:"859d0e24") {
			    versions {
			      patches {
			        url
			      }
			    }
			  }
			  ex4: repository(slug:"1bf99b87") {
			    versions {
			      patches {
			        url
			      }
			    }
			  }
			}
			""",
		};
		var resp = await gql.SendQueryAsync<object>(req);
		var results = new List<string>();
		var parse = (JObject)resp.Data;
		foreach (var (_, token) in parse)
		{
			foreach (var ver in token["versions"])
			{
				foreach (var patch in ver["patches"])
				{
					results.Add(patch["url"].Value<string>());
				}
			}
		}
		
		return results;
	}
}