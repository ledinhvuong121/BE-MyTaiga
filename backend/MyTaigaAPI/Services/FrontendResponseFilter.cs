using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace MyTaigaAPI.Services;
public class FrontendResponseFilter(ResponseEnricher enricher):IAsyncResultFilter,IExceptionFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context,ResultExecutionDelegate next)
    {
        if(context.Result is ObjectResult result&&result.StatusCode is null or <400&&result.Value is not null&&long.TryParse(context.HttpContext.User.FindFirst("sub")?.Value,out var user))
        {
            try{result.Value=await enricher.EnrichAsync(result.Value,user,context.HttpContext.RequestAborted);}
            catch(CloudMediaException e){context.Result=new ObjectResult(new{success=false,message=e.Message,data=(object?)null}){StatusCode=503};}
        }
        await next();
    }
    public void OnException(ExceptionContext context)
    {
        if(context.Exception is CloudMediaException e){context.Result=new ObjectResult(new{success=false,message=e.Message,data=(object?)null}){StatusCode=503};context.ExceptionHandled=true;}
    }
}
