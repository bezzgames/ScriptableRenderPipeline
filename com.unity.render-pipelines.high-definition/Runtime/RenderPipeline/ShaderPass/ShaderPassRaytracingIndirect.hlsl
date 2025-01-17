#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/RaytracingFragInputs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/RaytracingSampling.hlsl"

// Generic function that handles the reflection code
[shader("closesthit")]
void ClosestHitMain(inout RayIntersection rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
	// The first thing that we should do is grab the intersection vertice
    IntersectionVertex currentvertex;
    GetCurrentIntersectionVertex(attributeData, currentvertex);

    // Build the Frag inputs from the intersection vertice
    FragInputs fragInput;
    BuildFragInputsFromIntersection(currentvertex, rayIntersection.incidentDirection, fragInput);

    // Compute the view vector
    float3 viewWS = -rayIntersection.incidentDirection;
    float3 pointWSPos = GetAbsolutePositionWS(fragInput.positionRWS);

    // Make sure to add the additional travel distance
    float travelDistance = length(GetAbsolutePositionWS(fragInput.positionRWS) - rayIntersection.origin);
    rayIntersection.t = travelDistance;
    rayIntersection.cone.width += travelDistance * rayIntersection.cone.spreadAngle;

    PositionInputs posInput;
    posInput.positionWS = fragInput.positionRWS;
    posInput.positionSS = uint2(0, 0);

    // Build the surfacedata and builtindata
    SurfaceData surfaceData;
    BuiltinData builtinData;
    GetSurfaceDataFromIntersection(fragInput, viewWS, posInput, currentvertex, rayIntersection.cone, surfaceData, builtinData);

    // Compute the bsdf data
    BSDFData bsdfData =  ConvertSurfaceDataToBSDFData(posInput.positionSS, surfaceData);

#ifdef HAS_LIGHTLOOP
    // We do not want to use the diffuse when we compute the indirect diffuse
    #ifdef DIFFUSE_LIGHTING_ONLY
    builtinData.bakeDiffuseLighting = float3(0.0, 0.0, 0.0);
    builtinData.backBakeDiffuseLighting = float3(0.0, 0.0, 0.0);
    #endif

    // Compute the prelight data
    PreLightData preLightData = GetPreLightData(viewWS, posInput, bsdfData);
    float3 reflected = float3(0.0, 0.0, 0.0);
    float reflectedWeight = 0.0;

    #ifdef MULTI_BOUNCE_INDIRECT
    // We only launch a ray if there is still some depth be used
    if (rayIntersection.remainingDepth < _RaytracingMaxRecursion)
    {
        // Generate the new sample (follwing values of the sequence)
        float2 sample = float2(0.0, 0.0);
        sample.x = GetRaytracingNoiseSample(rayIntersection.sampleIndex, rayIntersection.remainingDepth * 2, rayIntersection.scramblingValue.x);
        sample.y = GetRaytracingNoiseSample(rayIntersection.sampleIndex, rayIntersection.remainingDepth * 2 + 1, rayIntersection.scramblingValue.y);

        #ifdef DIFFUSE_LIGHTING_ONLY
        // Importance sample with a cosine lobe
        float3 sampleDir = SampleHemisphereCosine(sample.x, sample.y, surfaceData.normalWS);

        // Create the ray descriptor for this pixel
        RayDesc rayDescriptor;
        rayDescriptor.Origin = pointWSPos + surfaceData.normalWS * _RaytracingRayBias;
        rayDescriptor.Direction = sampleDir;
        rayDescriptor.TMin = 0.0f;
        rayDescriptor.TMax = _RaytracingRayMaxLength;

        // Create and init the RayIntersection structure for this
        RayIntersection rayIntersection;
        rayIntersection.color = float3(0.0, 0.0, 0.0);
        rayIntersection.incidentDirection = rayDescriptor.Direction;
        rayIntersection.origin = rayDescriptor.Origin;
        rayIntersection.t = -1.0f;
        rayIntersection.remainingDepth = rayIntersection.remainingDepth + 1;

        // In order to achieve filtering for the textures, we need to compute the spread angle of the pixel
        rayIntersection.cone.spreadAngle = rayIntersection.cone.spreadAngle;
        rayIntersection.cone.width = rayIntersection.cone.width;

        // Evaluate the ray intersection
        TraceRay(_RaytracingAccelerationStructure, RAY_FLAG_CULL_BACK_FACING_TRIANGLES, RAYTRACING_OPAQUE_FLAG, 0, 1, 0, rayDescriptor, rayIntersection);

        // Contribute to the pixel
        builtinData.bakeDiffuseLighting = rayIntersection.color;
        #else
        // Importance sample the direction using GGX
        float3 sampleDir = float3(0.0, 0.0, 0.0);
        float roughness = PerceptualSmoothnessToRoughness(surfaceData.perceptualSmoothness);
        float NdotL, NdotH, VdotH;
        SampleGGXDir(sample, viewWS, fragInput.tangentToWorld, roughness, sampleDir, NdotL, NdotH, VdotH);

        // If the sample is under the surface
        if (dot(sampleDir, surfaceData.normalWS) > 0.0)
        {
            // Build the reflected ray
            RayDesc reflectedRay;
            reflectedRay.Origin = pointWSPos + surfaceData.normalWS * _RaytracingRayBias;
            reflectedRay.Direction = sampleDir;
            reflectedRay.TMin = 0;
            reflectedRay.TMax = _RaytracingRayMaxLength;

            // Create and init the RayIntersection structure for this
            RayIntersection reflectedIntersection;
            reflectedIntersection.color = float3(0.0, 0.0, 0.0);
            reflectedIntersection.incidentDirection = reflectedRay.Direction;
            reflectedIntersection.origin = reflectedRay.Origin;
            reflectedIntersection.t = -1.0f;
            reflectedIntersection.remainingDepth = rayIntersection.remainingDepth + 1;

            // In order to achieve filtering for the textures, we need to compute the spread angle of the pixel
            reflectedIntersection.cone.spreadAngle = rayIntersection.cone.spreadAngle;
            reflectedIntersection.cone.width = rayIntersection.cone.width;

            // Evaluate the ray intersection
            TraceRay(_RaytracingAccelerationStructure, RAY_FLAG_CULL_BACK_FACING_TRIANGLES, RAYTRACING_OPAQUE_FLAG, 0, 1, 0, reflectedRay, reflectedIntersection);

            // Override the transmitted color
            reflected = reflectedIntersection.color;
            reflectedWeight = 1.0;
        }
        #endif
    }
    #endif
    
    // Run the lightloop
    float3 diffuseLighting;
    float3 specularLighting;
    LightLoop(viewWS, posInput, preLightData, bsdfData, builtinData, reflectedWeight, 0.0, reflected,  float3(0.0, 0.0, 0.0), diffuseLighting, specularLighting);

    // Color display for the moment
    rayIntersection.color = diffuseLighting + specularLighting;
#else
    // Given that we will be multiplying the final color by the current exposure multiplier outside of this function, we need to make sure that
    // the unlit color is not impacted by that. Thus, we multiply it by the inverse of the current exposure multiplier.
    rayIntersection.color = bsdfData.color * GetInverseCurrentExposureMultiplier() + builtinData.emissiveColor;
#endif
}

// Generic function that handles the reflection code
[shader("anyhit")]
void AnyHitMain(inout RayIntersection rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    // The first thing that we should do is grab the intersection vertice
    IntersectionVertex currentvertex;
    GetCurrentIntersectionVertex(attributeData, currentvertex);

    // Build the Frag inputs from the intersection vertice
    FragInputs fragInput;
    BuildFragInputsFromIntersection(currentvertex, rayIntersection.incidentDirection, fragInput);

    // Compute the view vector
    float3 viewWS = -rayIntersection.incidentDirection;

    // Compute the distance of the ray
    float travelDistance = length(GetAbsolutePositionWS(fragInput.positionRWS) - rayIntersection.origin);
    rayIntersection.t = travelDistance;

    PositionInputs posInput;
    posInput.positionWS = fragInput.positionRWS;
    posInput.positionSS = uint2(0, 0);

    // Build the surfacedata and builtindata
    SurfaceData surfaceData;
    BuiltinData builtinData;
    bool isVisible = GetSurfaceDataFromIntersection(fragInput, viewWS, posInput, currentvertex, rayIntersection.cone, surfaceData, builtinData);

    // If this fella should be culled, then we cull it
    if(!isVisible)
    {
        IgnoreHit();
    }
}
