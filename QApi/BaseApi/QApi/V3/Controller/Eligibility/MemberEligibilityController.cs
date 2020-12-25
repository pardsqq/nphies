﻿using AutoMapper;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QBase.Controller;
using QDomain.Model.Eligibility;
using QInfrastructure.Api.Http.V3.Eligibility;
using QInfrastructure.Api.Service.V3.Eligibility.Query;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace QApi.V3.Controller.Eligibility
{
    [Produces("application/json")]
    [ApiVersion("3.0")]
    [AllowAnonymous]
    public class MemberEligibilityController : QBaseController
    {
        private readonly IMapper _mapper;
        private readonly IMediator _mediator;
        private readonly IHttpEligibility _http;

        public MemberEligibilityController(IMapper mapper, IMediator mediator, IHttpEligibility http)
        {
            _mapper = mapper;
            _mediator = mediator;
            _http = http;
        }

        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        [HttpPost, Route("Validate")]
        public async Task<ActionResult> Validate([FromBody] NAPHIES_Eligibility_Param param, [FromHeader] string BranchId)
        {
            var fhirSerialzer = new FhirJsonParser();
            Bundle resultBundle;
            bool? coverageInforce = false;
            List<string> errorCodes = new List<string>();
            List<string> errorMessages = new List<string>();

            try
            {
                var resultMediator = await _mediator.Send(new EligibilityValidateQuery
                {
                    param = param,
                    BranchId = BranchId
                });

                var httpResponse = _http._httpEligibility(resultMediator.bundleJson);


                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    var result = streamReader.ReadToEnd();

                    resultBundle = fhirSerialzer.Parse<Bundle>(result);

                    if (httpResponse.StatusCode == HttpStatusCode.Created || httpResponse.StatusCode == HttpStatusCode.OK)
                    {
                        foreach (var entry in resultBundle.Entry)
                        {
                            if (entry.Resource.TypeName == "CoverageEligibilityResponse")
                            {
                                var coverageEligibilityResponse = (CoverageEligibilityResponse)entry.Resource;

                                coverageInforce = coverageEligibilityResponse.Insurance[0].Inforce;
                                errorCodes = null;
                                errorMessages = null;
                            }
                        }
                    }
                }

                return Ok(new
                {
                    ReturnCode = errorCodes != null ? String.Join(",", errorCodes) : null,
                    ReturnMsgs = errorMessages != null ? String.Join(",", errorCodes) : null,
                    ReturnData = (bool)coverageInforce ? "The Coverage is Inforce" : "The Coverage is not inforce. Please check errors!"
                });

            }
            catch (WebException wex)
            {
                using (var errorResponse = (HttpWebResponse)wex.Response)
                {
                    using (var reader = new StreamReader(errorResponse.GetResponseStream()))
                    {
                        string errorContennt = reader.ReadToEnd().Trim();

                        resultBundle = fhirSerialzer.Parse<Bundle>(errorContennt);

                        foreach (var entry in resultBundle.Entry)
                        {
                            if (entry.Resource.TypeName == "CoverageEligibilityResponse")
                            {
                                var coverageEligibilityResponse = (CoverageEligibilityResponse)entry.Resource;

                                if (coverageEligibilityResponse.Error != null && coverageEligibilityResponse.Error.Count > 0)
                                {
                                    coverageInforce = coverageEligibilityResponse.Insurance[0].Inforce;

                                    foreach (var errorMsg in coverageEligibilityResponse.Error)
                                    {
                                        errorMessages.Add(errorMsg.Code.Text);
                                    }
                                }
                            }
                        }
                    }
                }

                return BadRequest(new
                {
                    ReturnCode = errorCodes != null ? String.Join(",", errorCodes) : null,
                    ReturnMsgs = errorMessages != null ? String.Join(",", errorCodes) : null,
                    ReturnData = (bool)coverageInforce ? "The Coverage is Inforce" : "The Coverage is not inforce. Please check errors!"
                });
            }
        }
    }
}