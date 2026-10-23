#pragma once
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#ifndef ARGUMENT_PRESENT
#define ARGUMENT_PRESENT(ArgumentPointer) ((CHAR*)(ArgumentPointer) != (CHAR*)NULL)
#endif

#include <winbio_types.h>
#include <winbio_adapter.h>
#include <winbio_err.h>
#include <strsafe.h>

// GUID for MFS100 WBF Engine Adapter
// {A328956F-43BB-4F15-B2A8-A63051012C79}
static const GUID GUID_MFS100_ENGINE_ADAPTER = 
{ 0xa328956f, 0x43bb, 0x4f15, { 0xb2, 0xa8, 0xa6, 0x30, 0x51, 0x01, 0x2c, 0x79 } };

#define MFS100_ENROLLMENT_STEPS_NEEDED 3

// Context for MFS100 Engine Adapter instance
typedef struct _MFS100_ENGINE_CONTEXT {
    UCHAR CurrentFeatureSet[8192];
    SIZE_T cbCurrentFeatureSet;
    UCHAR EnrollmentTemplate[16384];
    SIZE_T cbEnrollmentTemplate;
    DWORD dwEnrollmentCount;
    DWORD dwEnrollmentTarget;
    WINBIO_REJECT_DETAIL LastRejectDetail;
} MFS100_ENGINE_CONTEXT, *PMFS100_ENGINE_CONTEXT;
